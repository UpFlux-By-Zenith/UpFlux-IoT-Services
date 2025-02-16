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
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Protos;
using UpFlux.Gateway.Server.Repositories;
using FullVersionInfo = UpFlux.Gateway.Server.Models.FullVersionInfo;

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
        private readonly AlertingService _alertingService;
        private readonly IServiceProvider _serviceProvider;
        private readonly DeviceUsageAggregator _deviceUsageAggregator;

        // Single grpc channel for control messages
        private ControlChannelWorker _controlChannelWorker;

        private TcpListener _listener;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceCommunicationService"/> class.
        /// </summary>
        public DeviceCommunicationService(
            ILogger<DeviceCommunicationService> logger,
            IOptions<GatewaySettings> options,
            DeviceRepository deviceRepository,
            AlertingService alertingService,
            IServiceProvider serviceProvider,
            DeviceUsageAggregator deviceUsageAggregator)
        {
            _logger = logger;
            _settings = options.Value;
            _deviceRepository = deviceRepository;
            _alertingService = alertingService;
            _serviceProvider = serviceProvider;
            _deviceUsageAggregator = deviceUsageAggregator;
        }

        private ControlChannelWorker ControlChannelWorker =>
        _controlChannelWorker ??= _serviceProvider.GetRequiredService<ControlChannelWorker>();

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
                string uuidMessage = await RequestDeviceUuidAsync(networkStream).ConfigureAwait(false);
                if (!uuidMessage.StartsWith("UUID:"))
                {
                    _logger.LogWarning("Unexpected initial message from {remoteEndPoint}: {message}", remoteEndPoint, uuidMessage);
                    return;
                }

                string uuid = uuidMessage.Replace("UUID:", "").Trim();
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
            return response;
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
                        await ForwardMonitoringDataToCloudAsync(device, data).ConfigureAwait(false);
                        await SendMessageAsync(networkStream, "DATA_RECEIVED\n").ConfigureAwait(false);
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

            AlertMessage alert = new AlertMessage
            {
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                Level = "Information",
                Message = notification,
                Source = $"Device-{device.UUID}"
            };

            await ControlChannelWorker.SendAlertAsync(alert).ConfigureAwait(false);
        }

        /// <summary>
        /// Forwards monitoring data received from the device directly to the cloud.
        /// </summary>
        /// <param name="device">The device sending the data.</param>
        /// <param name="data">The raw monitoring data JSON string.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ForwardMonitoringDataToCloudAsync(Device device, string data)
        {
            try
            {
                MonitoringData monitoringData = JsonConvert.DeserializeObject<MonitoringData>(data);
                if (monitoringData == null || monitoringData.UUID != device.UUID)
                {
                    _logger.LogWarning("Invalid monitoring data received from device UUID: {uuid}", device.UUID);
                    return;
                }

                // ----------- Record usage for aggregator -----------
                double cpuPercent = monitoringData.Metrics.CpuMetrics.CurrentUsage;
                double memPercent = 0;
                if (monitoringData.Metrics.MemoryMetrics.TotalMemory > 0)
                {
                    memPercent = (monitoringData.Metrics.MemoryMetrics.UsedMemory /
                                  (double)monitoringData.Metrics.MemoryMetrics.TotalMemory) * 100.0;
                }
                double netSent = monitoringData.Metrics.NetworkMetrics.TransmittedBytes;
                double netRecv = monitoringData.Metrics.NetworkMetrics.ReceivedBytes;

                _deviceUsageAggregator.RecordUsage(device.UUID, cpuPercent, memPercent, netSent, netRecv);

                _logger.LogInformation("Forwarding monitoring data for device UUID: {uuid} to the cloud.", device.UUID);

                //Convert to the aggregated data format
                Protos.AggregatedData aggregatedData = TransformToAggregatedData(monitoringData);
                MonitoringDataMessage monMsg = new MonitoringDataMessage();
                monMsg.AggregatedData.Add(aggregatedData);

                // push it up
                await ControlChannelWorker.SendMonitoringDataAsync(monMsg);

                _logger.LogInformation("Monitoring data for device UUID: {uuid} forwarded to the cloud successfully.", device.UUID);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse monitoring data from device UUID: {uuid}", device.UUID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error forwarding monitoring data for device UUID: {uuid}", device.UUID);
            }
        }

        /// <summary>
        /// Transforms MonitoringData into AggregatedData format for the cloud.
        /// </summary>
        /// <param name="monitoringData">The monitoring data to transform.</param>
        /// <returns>The transformed AggregatedData object.</returns>
        private Protos.AggregatedData TransformToAggregatedData(MonitoringData monitoringData)
        {
            return new Protos.AggregatedData
            {
                Uuid = monitoringData.UUID,
                Timestamp = Timestamp.FromDateTime(monitoringData.Metrics.Timestamp.ToUniversalTime()),
                Metrics = new Metrics
                {
                    CpuUsage = monitoringData.Metrics.CpuMetrics.CurrentUsage,
                    MemoryUsage = monitoringData.Metrics.MemoryMetrics.UsedMemory /
                              (double)monitoringData.Metrics.MemoryMetrics.TotalMemory * 100,
                    DiskUsage = monitoringData.Metrics.DiskMetrics.UsedDiskSpace /
                            (double)monitoringData.Metrics.DiskMetrics.TotalDiskSpace * 100,
                    NetworkUsage = new Protos.NetworkUsage
                    {
                        BytesSent = monitoringData.Metrics.NetworkMetrics.TransmittedBytes,
                        BytesReceived = monitoringData.Metrics.NetworkMetrics.ReceivedBytes
                    },
                    CpuTemperature = monitoringData.Metrics.CpuTemperatureMetrics.TemperatureCelsius,
                    SystemUptime = monitoringData.Metrics.SystemUptimeMetrics.UptimeSeconds
                },
                SensorData = new Protos.SensorData
                {
                    RedValue = monitoringData.SensorData.RedValue,
                    GreenValue = monitoringData.SensorData.GreenValue,
                    BlueValue = monitoringData.SensorData.BlueValue
                }
            };
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
        public async Task<FullVersionInfo> RequestVersionInfoAsync(Device device)
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

                FullVersionInfo result = JsonConvert.DeserializeObject<FullVersionInfo>(responseMessage);
                return result;
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

                // Send "REQUEST_LOGS" command
                await SendMessageAsync(networkStream, "REQUEST_LOGS\n").ConfigureAwait(false);

                // Read the fileCount (4 bytes)
                byte[] fileCountBytes = new byte[4];
                int bytesRead = await networkStream.ReadAsync(fileCountBytes, 0, 4).ConfigureAwait(false);
                if (bytesRead < 4)
                {
                    throw new Exception("Failed to read the number of log files from device.");
                }

                int fileCount = BitConverter.ToInt32(fileCountBytes, 0);

                // if the device has zero log files, skip the rest
                if (fileCount == 0)
                {
                    _logger.LogInformation("Device {uuid} returned no logs.", deviceUuid);
                    return Array.Empty<string>();
                }

                _logger.LogInformation("Device {uuid} is sending {count} log file(s).", deviceUuid, fileCount);

                List<string> receivedLogFiles = new List<string>();

                for (int i = 0; i < fileCount; i++)
                {
                    // Read 4 bytes fileNameLength
                    byte[] fileNameLengthBytes = new byte[4];
                    bytesRead = await networkStream.ReadAsync(fileNameLengthBytes, 0, 4).ConfigureAwait(false);
                    if (bytesRead < 4)
                    {
                        throw new Exception("Failed to read the file name length.");
                    }
                    int fileNameLength = BitConverter.ToInt32(fileNameLengthBytes, 0);

                    // Read fileNameLength bytes fileName
                    byte[] fileNameBytes = new byte[fileNameLength];
                    bytesRead = await networkStream.ReadAsync(fileNameBytes, 0, fileNameLength).ConfigureAwait(false);
                    if (bytesRead < fileNameLength)
                    {
                        throw new Exception("Failed to read the file name.");
                    }
                    string fileName = Encoding.UTF8.GetString(fileNameBytes);

                    // Read 4 bytes logDataLength
                    byte[] lengthBytes = new byte[4];
                    bytesRead = await networkStream.ReadAsync(lengthBytes, 0, 4).ConfigureAwait(false);
                    if (bytesRead < 4)
                    {
                        throw new Exception("Failed to read the log file length.");
                    }
                    int logDataLength = BitConverter.ToInt32(lengthBytes, 0);

                    _logger.LogInformation(
                        "Receiving log file '{fileName}' of size {length} bytes from device {uuid}.",
                        fileName, logDataLength, deviceUuid
                    );

                    // Read the logData
                    byte[] logData = new byte[logDataLength];
                    int totalBytesRead = 0;
                    while (totalBytesRead < logDataLength)
                    {
                        bytesRead = await networkStream.ReadAsync(
                            logData, totalBytesRead, logDataLength - totalBytesRead
                        ).ConfigureAwait(false);

                        if (bytesRead == 0)
                        {
                            throw new Exception("Device closed the connection unexpectedly while sending logs.");
                        }
                        totalBytesRead += bytesRead;
                    }

                    // ave to local file
                    string logsDirectory = Path.Combine(_settings.LogsDirectory, "DeviceLogs");
                    Directory.CreateDirectory(logsDirectory);
                    string logFilePath = Path.Combine(
                        logsDirectory,
                        $"{deviceUuid}_{DateTime.UtcNow:yyyyMMddHHmmss}_{fileName}"
                    );

                    await File.WriteAllBytesAsync(logFilePath, logData).ConfigureAwait(false);

                    _logger.LogInformation(
                        "Log file '{fileName}' received from device {uuid} and saved to {path}.",
                        fileName, deviceUuid, logFilePath
                    );

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

        public async Task<bool> ValidateLicenseAsync(string uuid)
        {
            _logger.LogInformation("Validating license for device UUID: {uuid}", uuid);

            Device device = _deviceRepository.GetDeviceByUuid(uuid);

            if (device == null)
            {
                _logger.LogInformation("Device UUID: {uuid} not found. Initiating registration.", uuid);

                // send a new license request for registration
                await ControlChannelWorker.SendLicenseRequestAsync(uuid, isRenewal: false);
                // no license yet
                return false;
            }
            else
            {
                if (device.LicenseExpiration > DateTime.UtcNow)
                {
                    _logger.LogInformation("Device UUID: {uuid} has a valid license.", uuid);
                    return true;
                }

                if (device.NextEarliestRenewalAttempt > DateTime.UtcNow)
                {
                    _logger.LogWarning(
                        "License for device {uuid} is expired, but next renewal attempt is after {time}. Skipping renewal.",
                        uuid, device.NextEarliestRenewalAttempt
                    );
                    return false;
                }

                _logger.LogInformation("License for device UUID: {0} is expired. Initiating renewal now.", uuid);
                // send renewal request
                await ControlChannelWorker.SendLicenseRequestAsync(uuid, isRenewal: true);
            }

            // we do not have a valid license yet
            return false;
        }

        /// <summary>
        /// To set the license for a device in the database.
        /// </summary>
        public Device SetDeviceLicense(string uuid, string license, DateTime expiration)
        {
            Device device = _deviceRepository.GetDeviceByUuid(uuid);
            if (device == null) return null;
            device.License = license;
            device.LicenseExpiration = expiration;
            device.RegistrationStatus = "Registered";
            device.NextEarliestRenewalAttempt = DateTime.UtcNow; // reset
            _deviceRepository.AddOrUpdateDevice(device);
            return device;
        }

        /// <summary>
        /// Reschedules the license renewal for a device.
        /// </summary>
        /// <param name="uuid"></param>
        public void SetLicenseNotApproved(string uuid)
        {
            Device device = _deviceRepository.GetDeviceByUuid(uuid);
            if (device == null) return;
            device.NextEarliestRenewalAttempt = DateTime.UtcNow.AddMinutes(30);
            _deviceRepository.AddOrUpdateDevice(device);
        }

        /// <summary>
        /// Sends a license to a device by connecting as a client.
        /// </summary>
        public async Task<bool> SendLicenseToDeviceAsync(string uuid)
        {
            Device device = _deviceRepository.GetDeviceByUuid(uuid);
            if (device == null || string.IsNullOrWhiteSpace(device.License))
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
                await SendMessageAsync(networkStream, $"LICENSE:{device.License}\n").ConfigureAwait(false);
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
