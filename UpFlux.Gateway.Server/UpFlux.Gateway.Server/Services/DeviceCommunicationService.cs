﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Repositories;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for secure communication with devices over TCP using mTLS.
    /// Handles:
    /// - License validation
    /// - Monitoring data reception
    /// - Device notifications
    /// - Sending updates
    /// - Sending rollback commands
    /// - Requesting version information
    /// - Requesting logs
    /// </summary>
    public class DeviceCommunicationService
    {
        private readonly ILogger<DeviceCommunicationService> _logger;
        private readonly GatewaySettings _settings;
        private readonly LicenseValidationService _licenseValidationService;
        private readonly DeviceRepository _deviceRepository;
        private readonly DataAggregationService _dataAggregationService;
        private readonly AlertingService _alertingService;

        private readonly X509Certificate2 _serverCertificate;
        private readonly X509Certificate2 _trustedCaCertificate;
        private readonly X509Certificate2 _clientCertificate;

        private TcpListener _listener;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceCommunicationService"/> class.
        /// </summary>
        public DeviceCommunicationService(
            ILogger<DeviceCommunicationService> logger,
            IOptions<GatewaySettings> options,
            LicenseValidationService licenseValidationService,
            DeviceRepository deviceRepository,
            DataAggregationService dataAggregationService,
            AlertingService alertingService)
        {
            _logger = logger;
            _settings = options.Value;
            _licenseValidationService = licenseValidationService;
            _deviceRepository = deviceRepository;
            _dataAggregationService = dataAggregationService;
            _alertingService = alertingService;

            // Load certificates
            _serverCertificate = new X509Certificate2(_settings.CertificatePath, _settings.CertificatePassword);
            _trustedCaCertificate = new X509Certificate2(_settings.TrustedCaCertificatePath);

            // the same cert is used for both client and server roles for simplicity
            _clientCertificate = new X509Certificate2(_settings.CertificatePath, _settings.CertificatePassword);
        }

        /// <summary>
        /// Starts listening for incoming device connections server.
        /// </summary>
        public void StartListening()
        {
            _listener = new TcpListener(IPAddress.Any, _settings.TcpPort);
            _listener.Start();
            _logger.LogInformation("DeviceCommunicationService started listening on port {port}", _settings.TcpPort);
            AcceptConnectionsAsync();
        }

        /// <summary>
        /// Accepts incoming device connections asynchronously.
        /// </summary>
        private async void AcceptConnectionsAsync()
        {
            while (true)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleIncomingConnectionAsync(client));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error accepting incoming device connection.");
                }
            }
        }

        /// <summary>
        /// Handles an incoming device connection, performing TLS handshake and communication.
        /// </summary>
        private async Task HandleIncomingConnectionAsync(TcpClient client)
        {
            string remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            _logger.LogInformation("Accepted connection from device at {remoteEndPoint}", remoteEndPoint);

            try
            {
                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate);

                await sslStream.AuthenticateAsServerAsync(
                    _serverCertificate,
                    clientCertificateRequired: true,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false).ConfigureAwait(false);

                _logger.LogInformation("TLS handshake completed with device at {remoteEndPoint}", remoteEndPoint);

                await HandleDeviceCommunicationAsync(sslStream, remoteEndPoint).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling device connection from {remoteEndPoint}", remoteEndPoint);
            }
            finally
            {
                client.Close();
            }
        }

        /// <summary>
        /// Validates the device certificate during TLS handshake.
        /// </summary>
        private bool ValidateDeviceCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                _logger.LogWarning("SSL policy errors during device certificate validation: {errors}", sslPolicyErrors);
                return false;
            }

            chain.ChainPolicy.ExtraStore.Add(_trustedCaCertificate);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

            bool isValid = chain.Build((X509Certificate2)certificate);
            if (!isValid)
            {
                _logger.LogWarning("Device certificate chain validation failed: {errors}", chain.ChainStatus);
                return false;
            }

            string deviceUuid = GetUuidFromCertificate((X509Certificate2)certificate);
            if (string.IsNullOrEmpty(deviceUuid))
            {
                _logger.LogWarning("Device certificate does not contain a UUID in Subject.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Initiates a secure connection to a device
        /// </summary>
        public async Task InitiateSecureConnectionAsync(string ipAddress)
        {
            _logger.LogInformation("Initiating secure connection to device at IP: {ipAddress}", ipAddress);

            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(ipAddress), _settings.TcpPort);

                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate, SelectLocalCertificate);

                await sslStream.AuthenticateAsClientAsync(
                    ipAddress,
                    new X509CertificateCollection { _clientCertificate },
                    System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);

                _logger.LogInformation("TLS handshake completed with device at IP: {ipAddress}. Connection successful.", ipAddress);

                // Close after handshake test
                sslStream.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to establish secure connection with device at IP: {ipAddress}", ipAddress);
            }
        }

        /// <summary>
        /// Extracts the UUID from the device certificate's Subject (CN field).
        /// </summary>
        private string GetUuidFromCertificate(X509Certificate2 certificate)
        {
            const string cnPrefix = "CN=";
            string subject = certificate.Subject;
            if (subject.Contains(cnPrefix))
            {
                int startIndex = subject.IndexOf(cnPrefix) + cnPrefix.Length;
                int endIndex = subject.IndexOf(',', startIndex);
                return endIndex > -1 ? subject.Substring(startIndex, endIndex - startIndex) : subject.Substring(startIndex);
            }
            return null;
        }

        /// <summary>
        /// Handles communication with the device after successful TLS authentication.
        /// Requests device UUID, validates license, and continues if valid.
        /// </summary>
        private async Task HandleDeviceCommunicationAsync(SslStream sslStream, string remoteEndPoint)
        {
            try
            {
                string uuid = await RequestDeviceUuidAsync(sslStream).ConfigureAwait(false);
                _logger.LogInformation("Received UUID '{uuid}' from device at {remoteEndPoint}", uuid, remoteEndPoint);

                Device device = _deviceRepository.GetDeviceByUuid(uuid) ?? new Device
                {
                    UUID = uuid,
                    IPAddress = remoteEndPoint.Split(':')[0],
                    LastSeen = DateTime.UtcNow,
                    RegistrationStatus = "Pending"
                };

                device.IPAddress = remoteEndPoint.Split(':')[0];
                device.LastSeen = DateTime.UtcNow;
                _deviceRepository.AddOrUpdateDevice(device);

                bool isLicenseValid = await _licenseValidationService.ValidateLicenseAsync(uuid).ConfigureAwait(false);

                device = _deviceRepository.GetDeviceByUuid(uuid);
                if (isLicenseValid)
                {
                    _logger.LogInformation("Device UUID: {uuid} has a valid license. Proceeding with secure data exchange.", uuid);
                    await ProceedWithSecureDataExchangeAsync(sslStream, device).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning("Device UUID: {uuid} has invalid/expired license. Closing connection.", uuid);
                    await SendMessageAsync(sslStream, "LICENSE_INVALID\n").ConfigureAwait(false);
                    sslStream.Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during communication with device at {remoteEndPoint}", remoteEndPoint);
            }
        }

        /// <summary>
        /// Requests the device UUID.
        /// </summary>
        private async Task<string> RequestDeviceUuidAsync(SslStream sslStream)
        {
            await SendMessageAsync(sslStream, "REQUEST_UUID\n").ConfigureAwait(false);
            string response = await ReadMessageAsync(sslStream).ConfigureAwait(false);
            return response;
        }

        /// <summary>
        /// Reads a message from the SSL stream until a newline character is encountered.
        /// </summary>
        private async Task<string> ReadMessageAsync(SslStream sslStream)
        {
            StringBuilder messageData = new StringBuilder();
            byte[] buffer = new byte[1024];

            while (true)
            {
                int bytesRead = await sslStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (bytesRead == 0) break;

                string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageData.Append(chunk);
                if (chunk.Contains("\n"))
                    break;
            }

            return messageData.ToString().Trim();
        }

        /// <summary>
        /// Sends a message to the device over the SSL stream.
        /// </summary>
        private async Task SendMessageAsync(SslStream sslStream, string message)
        {
            byte[] msgBytes = Encoding.UTF8.GetBytes(message);
            await sslStream.WriteAsync(msgBytes, 0, msgBytes.Length).ConfigureAwait(false);
            await sslStream.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Handles ongoing communication once the device's license is validated.
        /// Processes monitoring data and device notifications.
        /// </summary>
        private async Task ProceedWithSecureDataExchangeAsync(SslStream sslStream, Device device)
        {
            try
            {
                while (true)
                {
                    string message = await ReadMessageAsync(sslStream).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(message))
                    {
                        _logger.LogInformation("Device UUID: {uuid} closed the connection.", device.UUID);
                        break;
                    }

                    _logger.LogInformation("Received message from device UUID: {uuid}: {message}", device.UUID, message);

                    if (message.StartsWith("MONITORING_DATA:"))
                    {
                        string data = message.Substring("MONITORING_DATA:".Length);
                        await ProcessDeviceDataAsync(device, data).ConfigureAwait(false);
                        await SendMessageAsync(sslStream, "DATA_RECEIVED\n").ConfigureAwait(false);
                    }
                    else if (message == "READY_FOR_LICENSE")
                    {
                        if (!string.IsNullOrWhiteSpace(device.License))
                            await SendLicenseAsync(device.UUID, device.License, sslStream).ConfigureAwait(false);
                        else
                            _logger.LogWarning("No license available to send to device {uuid}.", device.UUID);
                    }
                    else if (message.StartsWith("NOTIFICATION:"))
                    {
                        string notification = message.Substring("NOTIFICATION:".Length);
                        await HandleDeviceNotificationAsync(device, notification).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogWarning("Unknown message from device UUID: {uuid}: {message}", device.UUID, message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during data exchange with device UUID: {uuid}", device.UUID);
            }
        }

        /// <summary>
        /// Handles notifications from the device by creating an Alert and processing it.
        /// </summary>
        private async Task HandleDeviceNotificationAsync(Device device, string notification)
        {
            _logger.LogInformation("Received notification from device UUID: {uuid}: {notification}", device.UUID, notification);

            Alert alert = new Alert
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = "Information",
                Message = notification,
                Source = $"Device-{device.UUID}"
            };

            await _alertingService.ProcessDeviceLogAsync(alert).ConfigureAwait(false);
        }

        /// <summary>
        /// Processes monitoring data received from the device.
        /// </summary>
        private async Task ProcessDeviceDataAsync(Device device, string data)
        {
            try
            {
                MonitoringData monitoringData = JsonConvert.DeserializeObject<MonitoringData>(data);
                if (monitoringData == null || monitoringData.UUID != device.UUID)
                {
                    _logger.LogWarning("Invalid monitoring data received from device UUID: {uuid}", device.UUID);
                    return;
                }

                _dataAggregationService.AddMonitoringData(monitoringData);
                _logger.LogInformation("Monitoring data from device UUID: {uuid} processed successfully.", device.UUID);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse monitoring data from device UUID: {uuid}", device.UUID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing monitoring data from device UUID: {uuid}", device.UUID);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Sends a license to the device.
        /// </summary>
        private async Task SendLicenseAsync(string uuid, string license, SslStream sslStream)
        {
            try
            {
                string licenseMessage = $"LICENSE:{license}\n";
                await SendMessageAsync(sslStream, licenseMessage).ConfigureAwait(false);
                _logger.LogInformation("License sent to device UUID: {uuid}", uuid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send license to device UUID: {uuid}", uuid);
            }
        }

        /// <summary>
        /// Sends a license to a device by connecting as a client.
        /// </summary>
        public async Task<bool> SendLicenseToDeviceAsync(string uuid, string license)
        {
            Device device = _deviceRepository.GetDeviceByUuid(uuid);
            if (device == null)
            {
                _logger.LogWarning("Device with UUID '{uuid}' not found.", uuid);
                return false;
            }

            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.TcpPort);

                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate, SelectLocalCertificate);

                await sslStream.AuthenticateAsClientAsync(
                    device.IPAddress,
                    new X509CertificateCollection { _clientCertificate },
                    System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);

                _logger.LogInformation("TLS handshake completed with device at IP: {ipAddress}", device.IPAddress);

                // Directly send the license
                await SendMessageAsync(sslStream, $"LICENSE:{license}\n").ConfigureAwait(false);
                _logger.LogInformation("License sent to device UUID: {uuid}", uuid);
                return true;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send license to device UUID: {uuid}", uuid);
                return false;
            }
        }

        /// <summary>
        /// Sends an update package to a device.
        /// </summary>
        public async Task<bool> SendUpdatePackageAsync(string deviceUuid, string packageFilePath)
        {
            Device device = _deviceRepository.GetDeviceByUuid(deviceUuid);
            if (device == null)
            {
                _logger.LogWarning("Device with UUID '{uuid}' not found.", deviceUuid);
                return false;
            }

            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.TcpPort).ConfigureAwait(false);

                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate, SelectLocalCertificate);

                await sslStream.AuthenticateAsClientAsync(
                    device.IPAddress,
                    new X509CertificateCollection { _clientCertificate },
                    System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false).ConfigureAwait(false);

                _logger.LogInformation("TLS handshake completed with device at IP: {ipAddress}", device.IPAddress);

                string fileName = Path.GetFileName(packageFilePath);
                await SendMessageAsync(sslStream, $"SEND_PACKAGE:{fileName}\n").ConfigureAwait(false);

                string responseMessage = await ReadMessageAsync(sslStream).ConfigureAwait(false);
                if (responseMessage != "READY_FOR_PACKAGE")
                {
                    _logger.LogWarning("Device {uuid} is not ready for package transfer.", deviceUuid);
                    return false;
                }

                byte[] packageBytes = File.ReadAllBytes(packageFilePath);
                int packageLength = packageBytes.Length;

                byte[] lengthBytes = BitConverter.GetBytes(packageLength);
                await sslStream.WriteAsync(lengthBytes, 0, lengthBytes.Length).ConfigureAwait(false);
                await sslStream.FlushAsync().ConfigureAwait(false);

                await sslStream.WriteAsync(packageBytes, 0, packageBytes.Length).ConfigureAwait(false);
                await sslStream.FlushAsync().ConfigureAwait(false);

                _logger.LogInformation("Update package '{fileName}' sent to device {uuid}", fileName, deviceUuid);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send update package to device UUID: {uuid}", deviceUuid);
                return false;
            }
        }

        /// <summary>
        /// Sends a rollback command to a device.
        /// Parameters should include the version to rollback to.
        /// </summary>
        public async Task<bool> SendRollbackCommandAsync(string deviceUuid, string parameters)
        {
            Device device = _deviceRepository.GetDeviceByUuid(deviceUuid);
            if (device == null)
            {
                _logger.LogWarning("Device with UUID '{uuid}' not found.", deviceUuid);
                return false;
            }

            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.TcpPort).ConfigureAwait(false);

                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate, SelectLocalCertificate);

                await sslStream.AuthenticateAsClientAsync(
                    device.IPAddress,
                    new X509CertificateCollection { _clientCertificate },
                    System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false).ConfigureAwait(false);

                _logger.LogInformation("TLS handshake completed with device at IP: {ipAddress}", device.IPAddress);

                await SendMessageAsync(sslStream, $"ROLLBACK:{parameters}\n").ConfigureAwait(false);

                string responseMessage = await ReadMessageAsync(sslStream).ConfigureAwait(false);
                if (responseMessage != "ROLLBACK_INITIATED")
                {
                    _logger.LogWarning("Device {uuid} did not acknowledge rollback command.", deviceUuid);
                    return false;
                }

                // Wait for final confirmation from the device
                string confirmationMessage = await ReadMessageAsync(sslStream).ConfigureAwait(false);
                if (confirmationMessage == "ROLLBACK_COMPLETED")
                {
                    _logger.LogInformation("Device {uuid} reported rollback completed.", deviceUuid);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Device {uuid} failed to complete rollback.", deviceUuid);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send rollback command to device UUID: {uuid}", deviceUuid);
                return false;
            }
        }

        /// <summary>
        /// Requests version information from a device.
        /// Returns a list of VersionInfo objects representing versions installed on the device.
        /// </summary>
        public async Task<List<VersionInfo>> RequestVersionInfoAsync(Device device)
        {
            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.TcpPort).ConfigureAwait(false);

                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate);

                await sslStream.AuthenticateAsServerAsync(
                    _serverCertificate,
                    clientCertificateRequired: true,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false).ConfigureAwait(false);

                _logger.LogInformation("TLS handshake completed with device at IP: {ipAddress}", device.IPAddress);

                await SendMessageAsync(sslStream, "GET_VERSIONS\n").ConfigureAwait(false);

                byte[] buffer = new byte[8192];
                int bytesRead = await sslStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    _logger.LogWarning("Device {uuid} returned empty version information.", device.UUID);
                    return null;
                }

                string responseMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                if (string.IsNullOrWhiteSpace(responseMessage))
                {
                    _logger.LogWarning("Device {uuid} returned empty version information.", device.UUID);
                    return null;
                }

                List<string> versions = JsonConvert.DeserializeObject<List<string>>(responseMessage);
                if (versions == null || versions.Count == 0)
                {
                    _logger.LogWarning("Device {uuid} returned no versions.", device.UUID);
                    return null;
                }

                return versions.Select(version => new VersionInfo
                {
                    DeviceUUID = device.UUID,
                    Version = version,
                    InstalledAt = DateTime.UtcNow
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve version information from device UUID: {uuid}", device.UUID);
                return null;
            }
        }

        /// <summary>
        /// Requests logs from a device.
        /// Returns an array of file paths where the logs were saved.
        /// </summary>
        public async Task<string[]> RequestLogsAsync(string deviceUuid)
        {
            Device device = _deviceRepository.GetDeviceByUuid(deviceUuid);
            if (device == null)
            {
                _logger.LogWarning("Device with UUID '{uuid}' not found.", deviceUuid);
                return null;
            }

            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.TcpPort).ConfigureAwait(false);

                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate);

                await sslStream.AuthenticateAsServerAsync(
                    _serverCertificate,
                    clientCertificateRequired: true,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false).ConfigureAwait(false);

                _logger.LogInformation("TLS handshake completed with device at IP: {ipAddress}", device.IPAddress);

                await SendMessageAsync(sslStream, "REQUEST_LOGS\n").ConfigureAwait(false);

                byte[] fileCountBytes = new byte[4];
                int bytesRead = await sslStream.ReadAsync(fileCountBytes, 0, 4).ConfigureAwait(false);
                if (bytesRead < 4)
                {
                    throw new Exception("Failed to read the number of log files from device.");
                }

                int fileCount = BitConverter.ToInt32(fileCountBytes, 0);
                List<string> receivedLogFiles = new List<string>();

                for (int i = 0; i < fileCount; i++)
                {
                    byte[] fileNameLengthBytes = new byte[4];
                    bytesRead = await sslStream.ReadAsync(fileNameLengthBytes, 0, 4).ConfigureAwait(false);
                    if (bytesRead < 4)
                    {
                        throw new Exception("Failed to read the file name length.");
                    }

                    int fileNameLength = BitConverter.ToInt32(fileNameLengthBytes, 0);

                    byte[] fileNameBytes = new byte[fileNameLength];
                    bytesRead = await sslStream.ReadAsync(fileNameBytes, 0, fileNameLength).ConfigureAwait(false);
                    if (bytesRead < fileNameLength)
                    {
                        throw new Exception("Failed to read the file name.");
                    }

                    string fileName = Encoding.UTF8.GetString(fileNameBytes);

                    byte[] lengthBytes = new byte[4];
                    bytesRead = await sslStream.ReadAsync(lengthBytes, 0, 4).ConfigureAwait(false);
                    if (bytesRead < 4)
                    {
                        throw new Exception("Failed to read the log file length.");
                    }

                    int logDataLength = BitConverter.ToInt32(lengthBytes, 0);
                    _logger.LogInformation("Receiving log file '{fileName}' of size {length} bytes from device {uuid}.", fileName, logDataLength, deviceUuid);

                    byte[] logData = new byte[logDataLength];
                    int totalBytesRead = 0;
                    while (totalBytesRead < logDataLength)
                    {
                        bytesRead = await sslStream.ReadAsync(logData, totalBytesRead, logDataLength - totalBytesRead).ConfigureAwait(false);
                        if (bytesRead == 0)
                        {
                            throw new Exception("Device closed the connection unexpectedly while sending logs.");
                        }
                        totalBytesRead += bytesRead;
                    }

                    string logsDirectory = Path.Combine(_settings.LogsDirectory, "DeviceLogs");
                    Directory.CreateDirectory(logsDirectory);
                    string logFilePath = Path.Combine(logsDirectory, $"{deviceUuid}_{DateTime.UtcNow:yyyyMMddHHmmss}_{fileName}");

                    await File.WriteAllBytesAsync(logFilePath, logData).ConfigureAwait(false);

                    _logger.LogInformation("Log file '{fileName}' received from device {uuid} and saved to {path}.", fileName, deviceUuid, logFilePath);

                    receivedLogFiles.Add(logFilePath);
                }

                return receivedLogFiles.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve logs from device UUID: {uuid}", deviceUuid);
                return null;
            }
        }

        /// <summary>
        /// Local certificate selection callback used when this service acts as a client.
        /// Always return the client certificate.
        /// </summary>
        private X509Certificate SelectLocalCertificate(
            object sender,
            string targetHost,
            X509CertificateCollection localCertificates,
            X509Certificate remoteCertificate,
            string[] acceptableIssuers)
        {
            return _clientCertificate;
        }
    }
}
