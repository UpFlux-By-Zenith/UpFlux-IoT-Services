using System;
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
using UpFlux.Gateway.Server.Protos;
using UpFlux.Gateway.Server.Repositories;
using VersionInfo = UpFlux.Gateway.Server.Models.VersionInfo;

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
        private readonly DeviceRepository _deviceRepository;
        private readonly DataAggregationService _dataAggregationService;
        private readonly AlertingService _alertingService;
        private readonly CloudCommunicationService _cloudCommunicationService;

        private TcpListener _listener;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceCommunicationService"/> class.
        /// </summary>
        public DeviceCommunicationService(
            ILogger<DeviceCommunicationService> logger,
            IOptions<GatewaySettings> options,
            DeviceRepository deviceRepository,
            DataAggregationService dataAggregationService,
            AlertingService alertingService,
            CloudCommunicationService cloudCommunicationService)
        {
            _logger = logger;
            _settings = options.Value;
            _deviceRepository = deviceRepository;
            _dataAggregationService = dataAggregationService;
            _alertingService = alertingService;
            _cloudCommunicationService = cloudCommunicationService;
        }

        /// <summary>
        /// Starts listening for incoming device connections server.
        /// </summary>
        public void StartListening()
        {
            _listener = new TcpListener(IPAddress.Parse(_settings.GatewayServerIp), _settings.GatewayTcpPort);
            _listener.Start();
            _logger.LogInformation("DeviceCommunicationService started listening on port {port}", _settings.GatewayTcpPort);
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

                await HandleDeviceCommunicationAsync(networkStream, remoteEndPoint).ConfigureAwait(false);
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
        /// Initiates a secure connection to a device
        /// </summary>
        public async Task InitiateConnectionAsync(string ipAddress)
        {
            _logger.LogInformation("Initiating secure connection to device at IP: {ipAddress}", ipAddress);

            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(ipAddress), _settings.DeviceTcpPort);

                using NetworkStream networkStream = client.GetStream();

                // Send a simple handshake message
                //string handshakeMessage = "HANDSHAKE\n";
                //byte[] messageBytes = Encoding.UTF8.GetBytes(handshakeMessage);
                //await networkStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                //await networkStream.FlushAsync();

                _logger.LogInformation("Connection established with device at IP: {ipAddress}. Handshake successful.", ipAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to establish connection with device at IP: {ipAddress}", ipAddress);
            }
        }

        /// <summary>
        /// Handles communication with the device after a successful connection.
        /// </summary>
        private async Task HandleDeviceCommunicationAsync(NetworkStream networkStream, string remoteEndPoint)
        {
            try
            {
                string uuid = await RequestDeviceUuidAsync(networkStream).ConfigureAwait(false);
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

                bool isLicenseValid = await ValidateLicenseAsync(uuid);

                device = _deviceRepository.GetDeviceByUuid(uuid);
                if (isLicenseValid)
                {
                    _logger.LogInformation("Device UUID: {uuid} has a valid license. Proceeding with data exchange.", uuid);
                    await ProceedWithSecureDataExchangeAsync(networkStream, device).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning("Device UUID: {uuid} has invalid/expired license. Closing connection.", uuid);
                    await SendMessageAsync(networkStream, "LICENSE_INVALID\n").ConfigureAwait(false);
                    networkStream.Close();
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
        private async Task<string> RequestDeviceUuidAsync(NetworkStream networkStream)
        {
            await SendMessageAsync(networkStream, "REQUEST_UUID\n").ConfigureAwait(false);
            string response = await ReadMessageAsync(networkStream).ConfigureAwait(false);
            return response.Replace("UUID:", "").Trim();
        }

        /// <summary>
        /// Reads a message from the stream until a newline character is encountered.
        /// </summary>
        private async Task<string> ReadMessageAsync(NetworkStream networkStream)
        {
            StringBuilder messageData = new StringBuilder();
            byte[] buffer = new byte[1024];

            while (true)
            {
                int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (bytesRead == 0) break;

                string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageData.Append(chunk);
                if (chunk.Contains("\n"))
                    break;
            }

            return messageData.ToString().Trim();
        }

        /// <summary>
        /// Sends a message to the device over the network stream.
        /// </summary>
        private async Task SendMessageAsync(NetworkStream networkStream, string message)
        {
            byte[] msgBytes = Encoding.UTF8.GetBytes(message);
            await networkStream.WriteAsync(msgBytes, 0, msgBytes.Length).ConfigureAwait(false);
            await networkStream.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Handles ongoing communication once the device's license is validated.
        /// Processes monitoring data and device notifications.
        /// </summary>
        private async Task ProceedWithSecureDataExchangeAsync(NetworkStream networkStream, Device device)
        {
            try
            {
                while (true)
                {
                    string message = await ReadMessageAsync(networkStream).ConfigureAwait(false);
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
                        await SendMessageAsync(networkStream, "DATA_RECEIVED\n").ConfigureAwait(false);
                    }
                    else if (message == "READY_FOR_LICENSE")
                    {
                        if (!string.IsNullOrWhiteSpace(device.License))
                            await SendLicenseAsync(device.UUID, device.License, networkStream).ConfigureAwait(false);
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
        private async Task SendLicenseAsync(string uuid, string license, NetworkStream networkStream)
        {
            try
            {
                string licenseMessage = $"LICENSE:{license}\n";
                await SendMessageAsync(networkStream, licenseMessage).ConfigureAwait(false);
                _logger.LogInformation("License sent to device UUID: {uuid}", uuid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send license to device UUID: {uuid}", uuid);
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
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.DeviceTcpPort).ConfigureAwait(false);

                using NetworkStream networkStream = client.GetStream();

                string fileName = Path.GetFileName(packageFilePath);
                await SendMessageAsync(networkStream, $"SEND_PACKAGE:{fileName}\n").ConfigureAwait(false);

                string responseMessage = await ReadMessageAsync(networkStream).ConfigureAwait(false);
                if (responseMessage != "READY_FOR_PACKAGE")
                {
                    _logger.LogWarning("Device {uuid} is not ready for package transfer.", deviceUuid);
                    return false;
                }

                byte[] packageBytes = File.ReadAllBytes(packageFilePath);
                int packageLength = packageBytes.Length;

                byte[] lengthBytes = BitConverter.GetBytes(packageLength);
                await networkStream.WriteAsync(lengthBytes, 0, lengthBytes.Length).ConfigureAwait(false);
                await networkStream.FlushAsync().ConfigureAwait(false);

                await networkStream.WriteAsync(packageBytes, 0, packageBytes.Length).ConfigureAwait(false);
                await networkStream.FlushAsync().ConfigureAwait(false);

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
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.DeviceTcpPort).ConfigureAwait(false);

                using NetworkStream networkStream = client.GetStream();

                await SendMessageAsync(networkStream, $"ROLLBACK:{parameters}\n").ConfigureAwait(false);

                string responseMessage = await ReadMessageAsync(networkStream).ConfigureAwait(false);
                if (responseMessage != "ROLLBACK_INITIATED")
                {
                    _logger.LogWarning("Device {uuid} did not acknowledge rollback command.", deviceUuid);
                    return false;
                }

                // Wait for final confirmation from the device
                string confirmationMessage = await ReadMessageAsync(networkStream).ConfigureAwait(false);
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
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.DeviceTcpPort).ConfigureAwait(false);

                using NetworkStream networkStream = client.GetStream();

                await SendMessageAsync(networkStream, "GET_VERSIONS\n").ConfigureAwait(false);

                byte[] buffer = new byte[8192];
                int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
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
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.DeviceTcpPort).ConfigureAwait(false);

                using NetworkStream networkStream = client.GetStream();

                await SendMessageAsync(networkStream, "REQUEST_LOGS\n").ConfigureAwait(false);

                byte[] fileCountBytes = new byte[4];
                int bytesRead = await networkStream.ReadAsync(fileCountBytes, 0, 4).ConfigureAwait(false);
                if (bytesRead < 4)
                {
                    throw new Exception("Failed to read the number of log files from device.");
                }

                int fileCount = BitConverter.ToInt32(fileCountBytes, 0);
                List<string> receivedLogFiles = new List<string>();

                for (int i = 0; i < fileCount; i++)
                {
                    byte[] fileNameLengthBytes = new byte[4];
                    bytesRead = await networkStream.ReadAsync(fileNameLengthBytes, 0, 4).ConfigureAwait(false);
                    if (bytesRead < 4)
                    {
                        throw new Exception("Failed to read the file name length.");
                    }

                    int fileNameLength = BitConverter.ToInt32(fileNameLengthBytes, 0);

                    byte[] fileNameBytes = new byte[fileNameLength];
                    bytesRead = await networkStream.ReadAsync(fileNameBytes, 0, fileNameLength).ConfigureAwait(false);
                    if (bytesRead < fileNameLength)
                    {
                        throw new Exception("Failed to read the file name.");
                    }

                    string fileName = Encoding.UTF8.GetString(fileNameBytes);

                    byte[] lengthBytes = new byte[4];
                    bytesRead = await networkStream.ReadAsync(lengthBytes, 0, 4).ConfigureAwait(false);
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
                        bytesRead = await networkStream.ReadAsync(logData, totalBytesRead, logDataLength - totalBytesRead).ConfigureAwait(false);
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
        /// Validates the license for a device with the specified UUID.
        /// </summary>
        /// <param name="uuid">The device UUID.</param>
        /// <returns>A task representing the validation operation, with a boolean result indicating if the license is valid.</returns>
        //public async Task<bool> ValidateLicenseAsync(string uuid)
        //{
        //    _logger.LogInformation("Validating license for device UUID: {uuid}", uuid);

        //    Device device = _deviceRepository.GetDeviceByUuid(uuid);

        //    if (device == null)
        //    {
        //        _logger.LogInformation("Device UUID: {uuid} not found. Initiating registration.", uuid);
        //        await RegisterDeviceAsync(uuid);
        //    }
        //    else if (device.LicenseExpiration <= DateTime.UtcNow)
        //    {
        //        _logger.LogInformation("License for device UUID: {uuid} is expired or nearing expiration. Initiating renewal.", uuid);
        //        await RenewLicenseAsync(device);
        //    }
        //    else
        //    {
        //        _logger.LogInformation("Device UUID: {uuid} has a valid license.", uuid);
        //        return true;
        //    }

        //    // Refresh device info
        //    device = _deviceRepository.GetDeviceByUuid(uuid);

        //    // Return the license validity status
        //    return device.LicenseExpiration > DateTime.UtcNow;
        //}

        public async Task<bool> ValidateLicenseAsync(string uuid)
        {
            _logger.LogInformation("Validating license for device UUID: {uuid}", uuid);

            Device device = _deviceRepository.GetDeviceByUuid(uuid);

            if (device == null)
            {
                _logger.LogInformation("Device UUID: {uuid} not found. Initiating registration.", uuid);
                await RegisterDeviceAsync(uuid);
            }
            else
            {
                if (device.LicenseExpiration > DateTime.UtcNow)
                {
                    _logger.LogInformation("Device UUID: {uuid} has a valid license.", uuid);
                    return true;
                }

                if (device.NextEarliestRenewalAttempt.HasValue
                    && device.NextEarliestRenewalAttempt.Value > DateTime.UtcNow)
                {
                    _logger.LogWarning(
                        "License for device {uuid} is expired, but next renewal attempt is after {time}. Skipping renewal.",
                        uuid, device.NextEarliestRenewalAttempt.Value
                    );
                    return false;
                }

                _logger.LogInformation("License for device UUID: {uuid} is expired. Initiating renewal now.", uuid);
                await RenewLicenseAsync(device);
            }

            device = _deviceRepository.GetDeviceByUuid(uuid);
            return device.LicenseExpiration > DateTime.UtcNow;
        }

        /// <summary>
        /// Registers a new device by communicating with the cloud.
        /// </summary>
        /// <param name="uuid">The device UUID.</param>
        /// <returns>A task representing the registration operation.</returns>
        private async Task RegisterDeviceAsync(string uuid)
        {
            try
            {
                DeviceRegistrationResponse licenseResponse = await _cloudCommunicationService.RegisterDeviceAsync(uuid);

                if (licenseResponse.Approved)
                {
                    Device device = new Device
                    {
                        UUID = uuid,
                        License = licenseResponse.License,
                        LicenseExpiration = licenseResponse.ExpirationDate.ToDateTime()
                    };

                    _deviceRepository.AddOrUpdateDevice(device);

                    _logger.LogInformation("Device UUID: {uuid} registered successfully.", uuid);

                    // Send license to the device
                    bool success = await SendLicenseToDeviceAsync(uuid, licenseResponse.License);
                    if (!success)
                    {
                        _logger.LogWarning("Failed to push license to device {uuid}.", uuid);
                    }
                }
                else
                {
                    _logger.LogWarning("Device UUID: {uuid} registration was not approved by the cloud.", uuid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while registering device UUID: {uuid}", uuid);
            }
        }

        /// <summary>
        /// Renews the license for an existing device.
        /// </summary>
        /// <param name="device">The device whose license needs renewal.</param>
        /// <returns>A task representing the renewal operation.</returns>
        private async Task RenewLicenseAsync(Device device)
        {
            try
            {
                LicenseRenewalResponse renewalResponse = await _cloudCommunicationService.RenewLicenseAsync(device.UUID);

                if (renewalResponse.Approved)
                {
                    device.License = renewalResponse.License;
                    device.LicenseExpiration = renewalResponse.ExpirationDate.ToDateTime();
                    device.NextEarliestRenewalAttempt = null; // reseting because we have a valid license

                    _deviceRepository.AddOrUpdateDevice(device);

                    _logger.LogInformation("License for device UUID: {uuid} renewed successfully.", device.UUID);

                    // Send updated license to the device
                    bool success = await SendLicenseToDeviceAsync(device.UUID, renewalResponse.License);
                    if (!success)
                    {
                        _logger.LogWarning("Failed to push renewed license to device {uuid}.", device.UUID);
                    }
                }
                else
                {
                    _logger.LogWarning("License renewal for device UUID: {uuid} was not approved by the cloud.", device.UUID);
                    // Wait 30 minutes before next attempt
                    device.NextEarliestRenewalAttempt = DateTime.UtcNow.AddMinutes(30);
                    _deviceRepository.AddOrUpdateDevice(device);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while renewing license for device UUID: {uuid}", device.UUID);
                // Also wait 30 minutes before next attempt
                device.NextEarliestRenewalAttempt = DateTime.UtcNow.AddMinutes(30);
                _deviceRepository.AddOrUpdateDevice(device);
            }
        }

        /// <summary>
        /// Schedules periodic license checks and renewals.
        /// </summary>
        /// <param name="stoppingToken">Token to signal cancellation.</param>
        /// <returns>A task representing the scheduled operation.</returns>
        public async Task ScheduleLicenseRenewalsAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting scheduled license renewals.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    List<Device> devices = _deviceRepository.GetAllDevices();

                    foreach (Device device in devices)
                    {
                        // Check if the license is expiring within the next day
                        if (device.LicenseExpiration <= DateTime.UtcNow.AddDays(1))
                        {
                            _logger.LogInformation("License for device UUID: {uuid} is nearing expiration.", device.UUID);
                            await RenewLicenseAsync(device);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred during scheduled license renewals.");
                }

                await Task.Delay(TimeSpan.FromMinutes(_settings.LicenseCheckIntervalMinutes), stoppingToken);
            }

            _logger.LogInformation("Scheduled license renewals are stopping.");
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
                await client.ConnectAsync(IPAddress.Parse(device.IPAddress), _settings.DeviceTcpPort);

                using NetworkStream networkStream = client.GetStream();

                // Directly send the license
                await SendMessageAsync(networkStream, $"LICENSE:{license}\n").ConfigureAwait(false);
                _logger.LogInformation("License sent to device UUID: {uuid}", uuid);
                return true;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send license to device UUID: {uuid}", uuid);
                return false;
            }
        }
    }
}
