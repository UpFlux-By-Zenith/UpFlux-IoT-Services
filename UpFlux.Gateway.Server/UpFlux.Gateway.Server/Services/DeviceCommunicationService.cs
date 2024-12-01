using System;
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
    /// </summary>
    public class DeviceCommunicationService
    {
        private readonly ILogger<DeviceCommunicationService> _logger;
        private readonly GatewaySettings _settings;
        private readonly LicenseValidationService _licenseValidationService;
        private readonly DeviceRepository _deviceRepository;
        private readonly X509Certificate2 _serverCertificate;
        private readonly X509Certificate2 _trustedCaCertificate;

        private readonly DataAggregationService _dataAggregationService;
        private readonly AlertingService _alertingService;

        private TcpListener _listener;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceCommunicationService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="settings">Gateway settings.</param>
        /// <param name="licenseValidationService">Service for license validation.</param>
        /// <param name="deviceRepository">Repository for device data.</param>
        public DeviceCommunicationService(
            ILogger<DeviceCommunicationService> logger,
            IOptions<GatewaySettings> settings,
            LicenseValidationService licenseValidationService,
            DeviceRepository deviceRepository,
            DataAggregationService dataAggregationService,
            AlertingService alertingService)
        {
            _logger = logger;
            _settings = settings.Value;
            _licenseValidationService = licenseValidationService;
            _deviceRepository = deviceRepository;

            // Load server certificate
            _serverCertificate = new X509Certificate2(_settings.CertificatePath, _settings.CertificatePassword);

            // Load trusted CA certificate
            _trustedCaCertificate = new X509Certificate2(_settings.TrustedCaCertificatePath);
            _dataAggregationService = dataAggregationService;
        }

        /// <summary>
        /// Starts listening for incoming device connections.
        /// </summary>
        public void StartListening()
        {
            _listener = new TcpListener(IPAddress.Any, _settings.TcpPort);
            _listener.Start();
            _logger.LogInformation("DeviceCommunicationService started listening on port {port}", _settings.TcpPort);

            // Begin accepting connections asynchronously
            AcceptConnectionsAsync();
        }

        /// <summary>
        /// Initiates a secure connection with a device at the specified IP address.
        /// </summary>
        /// <param name="ipAddress">The IP address of the device.</param>
        /// <returns>A task representing the connection operation.</returns>
        public async Task InitiateSecureConnectionAsync(string ipAddress)
        {
            _logger.LogInformation("Initiating secure connection to device at IP: {ipAddress}", ipAddress);

            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(ipAddress), _settings.TcpPort);

                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate);

                // Authenticate as server
                await sslStream.AuthenticateAsServerAsync(
                    _serverCertificate,
                    clientCertificateRequired: true,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);

                _logger.LogInformation("TLS handshake completed with device at IP: {ipAddress}", ipAddress);

                // Handle communication
                await HandleDeviceCommunicationAsync(sslStream, ipAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to establish secure connection with device at IP: {ipAddress}", ipAddress);
            }
        }

        /// <summary>
        /// Accepts incoming connections from devices.
        /// </summary>
        private async void AcceptConnectionsAsync()
        {
            while (true)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleIncomingConnectionAsync(client));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error accepting incoming device connection.");
                }
            }
        }

        /// <summary>
        /// Handles an incoming device connection.
        /// </summary>
        /// <param name="client">The connected TCP client.</param>
        /// <returns>A task representing the handling operation.</returns>
        private async Task HandleIncomingConnectionAsync(TcpClient client)
        {
            string? remoteEndPoint = client.Client.RemoteEndPoint.ToString();
            _logger.LogInformation("Accepted connection from device at {remoteEndPoint}", remoteEndPoint);

            try
            {
                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate);

                // Authenticate as server
                await sslStream.AuthenticateAsServerAsync(
                    _serverCertificate,
                    clientCertificateRequired: true,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);

                _logger.LogInformation("TLS handshake completed with device at {remoteEndPoint}", remoteEndPoint);

                // Handle communication
                await HandleDeviceCommunicationAsync(sslStream, remoteEndPoint);
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
        /// Validates the device's certificate during the TLS handshake.
        /// </summary>
        /// <param name="sender">The sender object.</param>
        /// <param name="certificate">The certificate presented by the device.</param>
        /// <param name="chain">The certificate chain.</param>
        /// <param name="sslPolicyErrors">Any SSL policy errors detected.</param>
        /// <returns>True if the certificate is valid; otherwise, false.</returns>
        private bool ValidateDeviceCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // Custom validation logic
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                // Verify certificate chain
                chain.ChainPolicy.ExtraStore.Add(_trustedCaCertificate);
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                // Certificate Revocation Check during TLS handshake
                // chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                // chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;

                bool isValid = chain.Build((X509Certificate2)certificate);
                if (!isValid)
                {
                    _logger.LogWarning("Device certificate chain validation failed: {errors}", chain.ChainStatus);
                    return false;
                }

                // Extract UUID from certificate
                string deviceUuid = GetUuidFromCertificate((X509Certificate2)certificate);
                if (string.IsNullOrEmpty(deviceUuid))
                {
                    _logger.LogWarning("Device certificate does not contain UUID in Subject or SAN.");
                    return false;
                }

                return true;
            }
            else
            {
                _logger.LogWarning("SSL policy errors during device certificate validation: {errors}", sslPolicyErrors);
                return false;
            }
        }

        /// <summary>
        /// Extracts the UUID from the device's certificate.
        /// </summary>
        /// <param name="certificate">The device's certificate.</param>
        /// <returns>The UUID if found; otherwise, null.</returns>
        private string GetUuidFromCertificate(X509Certificate2 certificate)
        {
            // Extract UUID from Subject (like "CN=deviceUUID")
            string subject = certificate.Subject;
            string cnPrefix = "CN=";
            if (subject.Contains(cnPrefix))
            {
                int startIndex = subject.IndexOf(cnPrefix) + cnPrefix.Length;
                int endIndex = subject.IndexOf(',', startIndex);
                string uuid = endIndex > -1 ? subject.Substring(startIndex, endIndex - startIndex) : subject.Substring(startIndex);
                return uuid;
            }

            return null;
        }

        /// <summary>
        /// Handles communication with the device after successful authentication.
        /// </summary>
        /// <param name="sslStream">The SSL stream for communication.</param>
        /// <param name="remoteEndPoint">The remote endpoint of the device.</param>
        /// <returns>A task representing the communication operation.</returns>
        private async Task HandleDeviceCommunicationAsync(SslStream sslStream, string remoteEndPoint)
        {
            try
            {
                // Request device UUID
                string uuid = await RequestDeviceUuidAsync(sslStream);

                _logger.LogInformation("Received UUID '{uuid}' from device at {remoteEndPoint}", uuid, remoteEndPoint);

                // Update device information
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

                // Validate license
                bool isLicenseValid = await _licenseValidationService.ValidateLicenseAsync(uuid);

                // Refresh device info after validation
                device = _deviceRepository.GetDeviceByUuid(uuid);

                // Check if license is valid
                if (isLicenseValid)
                {
                    _logger.LogInformation("Device UUID: {uuid} has a valid license. Proceeding with secure data exchange.", uuid);

                    // Proceed with secure data exchange
                    await ProceedWithSecureDataExchangeAsync(sslStream, device);
                }
                else
                {
                    _logger.LogWarning("Device UUID: {uuid} has an invalid or expired license. Closing connection.", uuid);

                    // pass to the device about invalid license
                    string message = "LICENSE_INVALID\n";
                    byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(message);
                    await sslStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                    await sslStream.FlushAsync();

                    // Close the connection
                    sslStream.Close();
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during communication with device at {remoteEndPoint}", remoteEndPoint);
            }
        }

        /// <summary>
        /// Requests the UUID from the connected device.
        /// </summary>
        private async Task<string> RequestDeviceUuidAsync(SslStream sslStream)
        {
            // Send request for UUID
            string requestMessage = "REQUEST_UUID\n";
            byte[] requestBytes = Encoding.UTF8.GetBytes(requestMessage);
            await sslStream.WriteAsync(requestBytes, 0, requestBytes.Length);
            await sslStream.FlushAsync();

            // Read response
            string responseMessage = await ReadMessageAsync(sslStream);

            return responseMessage;
        }

        /// <summary>
        /// Reads a message from the SSL stream terminated by a newline character.
        /// </summary>
        private async Task<string> ReadMessageAsync(SslStream sslStream)
        {
            StringBuilder messageData = new StringBuilder();
            byte[] buffer = new byte[1024];
            int bytesRead = -1;

            do
            {
                bytesRead = await sslStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break;
                }

                string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageData.Append(chunk);

                if (chunk.Contains("\n"))
                {
                    break;
                }
            } while (bytesRead != 0);

            return messageData.ToString().Trim();
        }

        /// <summary>
        /// Handles secure data exchange with the device after license validation.
        /// </summary>
        private async Task ProceedWithSecureDataExchangeAsync(SslStream sslStream, Device device)
        {
            try
            {
                // Receive commands or data from the device
                while (true)
                {
                    string message = await ReadMessageAsync(sslStream);
                    if (string.IsNullOrEmpty(message))
                    {
                        _logger.LogInformation("Device UUID: {uuid} closed the connection.", device.UUID);
                        break;
                    }

                    _logger.LogInformation("Received message from device UUID: {uuid}: {message}", device.UUID, message);

                    if (message.StartsWith("MONITORING_DATA:"))
                    {
                        // Process monitoring data
                        string data = message.Substring("MONITORING_DATA:".Length);
                        await ProcessDeviceDataAsync(device, data);

                        // Send acknowledgment
                        string acknowledgment = "DATA_RECEIVED\n";
                        byte[] ackBytes = Encoding.UTF8.GetBytes(acknowledgment);
                        await sslStream.WriteAsync(ackBytes, 0, ackBytes.Length);
                        await sslStream.FlushAsync();
                    }
                    else if (message == "READY_FOR_LICENSE")
                    {
                        // Send license to the device
                        await SendLicenseAsync(device.UUID, device.License, sslStream);
                    }
                    else if (message == "REQUEST_UPDATE")
                    {
                        // Handle update request
                        await SendUpdatePackageAsync(device.UUID, sslStream);
                    }
                    else if (message.StartsWith("NOTIFICATION:"))
                    {
                        // Handle notifications from the device
                        string notification = message.Substring("NOTIFICATION:".Length);
                        await HandleDeviceNotificationAsync(device, notification);
                    }
                    else
                    {
                        _logger.LogWarning("Unknown message received from device UUID: {uuid}", device.UUID);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during data exchange with device UUID: {uuid}", device.UUID);
            }
        }

        /// <summary>
        /// Method to handle notifications received from the device.
        /// </summary>
        /// <param name="device">The device that sent the notification.</param>
        /// <param name="notification">String containing the notification message.</param>
        /// <returns>Returns a task representing the asynchronous operation.</returns>
        private async Task HandleDeviceNotificationAsync(Device device, string notification)
        {
            _logger.LogInformation("Received notification from device UUID: {uuid}: {notification}", device.UUID, notification);

            // Create an Alert object
            Alert alert = new Alert
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = "Information",
                Message = notification,
                Source = $"Device-{device.UUID}"
            };

            // Process the log event using AlertingService
            await _alertingService.ProcessDeviceLogAsync(alert);
        }

        /// <summary>
        /// Processes data received from the device.
        /// </summary>
        private async Task ProcessDeviceDataAsync(Device device, string data)
        {
            try
            {
                // Deserialize the JSON data into MonitoringData object
                MonitoringData monitoringData = JsonConvert.DeserializeObject<MonitoringData>(data);

                // Validate the data
                if (monitoringData == null || monitoringData.UUID != device.UUID)
                {
                    _logger.LogWarning("Invalid monitoring data received from device UUID: {uuid}", device.UUID);
                    return;
                }

                // Add data to aggregation service
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
        /// Sends a license to the device over the existing SSL stream.
        /// </summary>
        private async Task SendLicenseAsync(string uuid, string license, SslStream sslStream)
        {
            try
            {
                // Send license
                string licenseMessage = $"LICENSE:{license}\n";
                byte[] licenseBytes = Encoding.UTF8.GetBytes(licenseMessage);
                await sslStream.WriteAsync(licenseBytes, 0, licenseBytes.Length);
                await sslStream.FlushAsync();

                _logger.LogInformation("License sent to device UUID: {uuid}", uuid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send license to device UUID: {uuid}", uuid);
            }
        }

        /// <summary>
        /// Sends an update package to a device over the existing SSL stream.
        /// </summary>
        private async Task SendUpdatePackageAsync(string deviceUuid, SslStream sslStream)
        {
            Device device = _deviceRepository.GetDeviceByUuid(deviceUuid);
            if (device == null)
            {
                _logger.LogWarning("Device with UUID '{uuid}' not found.", deviceUuid);
                return;
            }

            try
            {
                // Send update package command with filename
                string packageFilePath = GetLatestPackageFilePath();
                if (string.IsNullOrEmpty(packageFilePath))
                {
                    _logger.LogWarning("No update package available to send.");
                    return;
                }

                string fileName = Path.GetFileName(packageFilePath);
                string commandMessage = $"SEND_PACKAGE:{fileName}\n";
                byte[] commandBytes = Encoding.UTF8.GetBytes(commandMessage);
                await sslStream.WriteAsync(commandBytes, 0, commandBytes.Length);
                await sslStream.FlushAsync();

                // Wait for device acknowledgment
                string responseMessage = await ReadMessageAsync(sslStream);
                if (responseMessage != "READY_FOR_PACKAGE")
                {
                    _logger.LogWarning("Device {uuid} is not ready for package transfer.", deviceUuid);
                    return;
                }

                // Read the package data
                byte[] packageBytes = File.ReadAllBytes(packageFilePath);
                int packageLength = packageBytes.Length;

                // Send the length of the package (as 4-byte integer)
                byte[] lengthBytes = BitConverter.GetBytes(packageLength);
                await sslStream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                await sslStream.FlushAsync();

                // Send the package data
                await sslStream.WriteAsync(packageBytes, 0, packageBytes.Length);
                await sslStream.FlushAsync();

                _logger.LogInformation("Update package '{fileName}' sent to device {uuid}", fileName, deviceUuid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send update package to device UUID: {uuid}", deviceUuid);
            }
        }

        /// <summary>
        /// Gets the file path of the latest update package.
        /// </summary>
        private string GetLatestPackageFilePath()
        {
            string packageDirectory = _settings.UpdatePackageDirectory;
            if (!Directory.Exists(packageDirectory))
            {
                _logger.LogWarning("Package directory does not exist: {path}", packageDirectory);
                return null;
            }

            string[] packageFiles = Directory.GetFiles(packageDirectory, "*.deb");
            if (packageFiles.Length == 0)
            {
                _logger.LogWarning("No package files found in directory: {path}", packageDirectory);
                return null;
            }

            // Return the latest package based on creation time
            return packageFiles.OrderByDescending(f => File.GetCreationTime(f)).FirstOrDefault();
        }

        /// <summary>
        /// Sends a rollback command to a device.
        /// </summary>
        /// <param name="deviceUuid">The UUID of the device.</param>
        /// <param name="parameters">Parameters for the rollback command (e.g., version to rollback to).</param>
        /// <returns>A task representing the asynchronous operation, returning true if successful.</returns>
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
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.TcpPort);

                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate);

                // Authenticate as server
                await sslStream.AuthenticateAsServerAsync(
                    _serverCertificate,
                    clientCertificateRequired: true,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);

                _logger.LogInformation("TLS handshake completed with device at IP: {ipAddress}", device.IPAddress);

                // Send rollback command
                string commandMessage = $"ROLLBACK:{parameters}\n";
                byte[] commandBytes = System.Text.Encoding.UTF8.GetBytes(commandMessage);
                await sslStream.WriteAsync(commandBytes, 0, commandBytes.Length);
                await sslStream.FlushAsync();

                // Wait for device acknowledgment
                byte[] buffer = new byte[1024];
                int bytesRead = await sslStream.ReadAsync(buffer, 0, buffer.Length);
                string responseMessage = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                if (responseMessage != "ROLLBACK_INITIATED")
                {
                    _logger.LogWarning("Device {uuid} did not acknowledge rollback command.", deviceUuid);
                    return false;
                }

                _logger.LogInformation("Rollback command acknowledged by device {uuid}", deviceUuid);

                // To Implement that will confirm that roll back has been completed

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send rollback command to device UUID: {uuid}", deviceUuid);
                return false;
            }
        }

        /// <summary>
        /// Requests software versions from a device.
        /// </summary>
        /// <param name="device">The device to query.</param>
        /// <returns>A task representing the asynchronous operation, returning the VersionInfo.</returns>
        public async Task<List<VersionInfo>> RequestVersionInfoAsync(Device device)
        {
            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.TcpPort);

                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate);

                // Authenticate as server
                await sslStream.AuthenticateAsServerAsync(
                    _serverCertificate,
                    clientCertificateRequired: true,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);

                _logger.LogInformation("TLS handshake completed with device at IP: {ipAddress}", device.IPAddress);

                // Send version request command
                string commandMessage = "GET_VERSIONS\n";
                byte[] commandBytes = Encoding.UTF8.GetBytes(commandMessage);
                await sslStream.WriteAsync(commandBytes, 0, commandBytes.Length);
                await sslStream.FlushAsync();

                // Wait for device response
                byte[] buffer = new byte[8192]; // Increase buffer size if needed
                int bytesRead = await sslStream.ReadAsync(buffer, 0, buffer.Length);
                string responseMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                if (string.IsNullOrWhiteSpace(responseMessage))
                {
                    _logger.LogWarning("Device {uuid} returned empty version information.", device.UUID);
                    return null;
                }

                // Deserialize the JSON string into a list of versions
                List<string> versions = JsonConvert.DeserializeObject<List<string>>(responseMessage);

                if (versions == null || versions.Count == 0)
                {
                    _logger.LogWarning("Device {uuid} returned no versions.", device.UUID);
                    return null;
                }

                // Create a list of VersionInfo objects
                List<VersionInfo> versionInfoList = versions.Select(version => new VersionInfo
                {
                    DeviceUUID = device.UUID,
                    Version = version,
                    InstalledAt = DateTime.UtcNow // Or retrieve actual installation time if available
                }).ToList();

                return versionInfoList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve version information from device UUID: {uuid}", device.UUID);
                return null;
            }
        }

        /// <summary>
        /// Requests logs from a device.
        /// </summary>
        /// <param name="deviceUuid">The UUID of the device.</param>
        /// <returns>A task representing the asynchronous operation, returning the path to the received log file.</returns>
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
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.TcpPort);

                using NetworkStream networkStream = client.GetStream();
                using SslStream sslStream = new SslStream(networkStream, false, ValidateDeviceCertificate);

                // Authenticate as server
                await sslStream.AuthenticateAsServerAsync(
                    _serverCertificate,
                    clientCertificateRequired: true,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);

                _logger.LogInformation("TLS handshake completed with device at IP: {ipAddress}", device.IPAddress);

                // Send log request command
                string commandMessage = "REQUEST_LOGS\n";
                byte[] commandBytes = Encoding.UTF8.GetBytes(commandMessage);
                await sslStream.WriteAsync(commandBytes, 0, commandBytes.Length);
                await sslStream.FlushAsync();

                // Receive the number of log files
                byte[] fileCountBytes = new byte[4];
                int bytesRead = await sslStream.ReadAsync(fileCountBytes, 0, 4);
                if (bytesRead < 4)
                {
                    throw new Exception("Failed to read the number of log files.");
                }

                int fileCount = BitConverter.ToInt32(fileCountBytes, 0);

                var receivedLogFiles = new List<string>();

                for (int i = 0; i < fileCount; i++)
                {
                    // Read the file name length and file name
                    byte[] fileNameLengthBytes = new byte[4];
                    bytesRead = await sslStream.ReadAsync(fileNameLengthBytes, 0, 4);
                    if (bytesRead < 4)
                    {
                        throw new Exception("Failed to read the file name length.");
                    }

                    int fileNameLength = BitConverter.ToInt32(fileNameLengthBytes, 0);

                    byte[] fileNameBytes = new byte[fileNameLength];
                    bytesRead = await sslStream.ReadAsync(fileNameBytes, 0, fileNameLength);
                    if (bytesRead < fileNameLength)
                    {
                        throw new Exception("Failed to read the file name.");
                    }

                    string fileName = Encoding.UTF8.GetString(fileNameBytes);

                    // Read the log file length
                    byte[] lengthBytes = new byte[4];
                    bytesRead = await sslStream.ReadAsync(lengthBytes, 0, 4);
                    if (bytesRead < 4)
                    {
                        throw new Exception("Failed to read the log file length.");
                    }

                    int logDataLength = BitConverter.ToInt32(lengthBytes, 0);

                    _logger.LogInformation("Receiving log file '{fileName}' of size {length} bytes from device {uuid}.", fileName, logDataLength, deviceUuid);

                    // Read the log data
                    byte[] logData = new byte[logDataLength];
                    int totalBytesRead = 0;
                    while (totalBytesRead < logDataLength)
                    {
                        bytesRead = await sslStream.ReadAsync(logData, totalBytesRead, logDataLength - totalBytesRead);
                        if (bytesRead == 0)
                        {
                            throw new Exception("Device closed the connection unexpectedly.");
                        }
                        totalBytesRead += bytesRead;
                    }

                    // Save the log data to a file
                    string logsDirectory = Path.Combine(_settings.LogsDirectory, "DeviceLogs");
                    Directory.CreateDirectory(logsDirectory);
                    string logFilePath = Path.Combine(logsDirectory, $"{deviceUuid}_{DateTime.UtcNow:yyyyMMddHHmmss}_{fileName}");

                    await File.WriteAllBytesAsync(logFilePath, logData);

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
    }
}
