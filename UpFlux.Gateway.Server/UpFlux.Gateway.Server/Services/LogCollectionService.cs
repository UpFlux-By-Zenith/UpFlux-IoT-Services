using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for collecting logs from devices and sending them to the cloud.
    /// </summary>
    public class LogCollectionService
    {
        private readonly ILogger<LogCollectionService> _logger;
        private readonly DeviceCommunicationService _deviceCommunicationService;
        private readonly CloudCommunicationService _cloudCommunicationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="LogCollectionService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="deviceCommunicationService">Device communication service.</param>
        /// <param name="cloudCommunicationService">Cloud communication service.</param>
        public LogCollectionService(
            ILogger<LogCollectionService> logger,
            DeviceCommunicationService deviceCommunicationService,
            CloudCommunicationService cloudCommunicationService)
        {
            _logger = logger;
            _deviceCommunicationService = deviceCommunicationService;
            _cloudCommunicationService = cloudCommunicationService;
        }

        /// <summary>
        /// Initiates log collection from a device and sends the logs to the cloud.
        /// </summary>
        /// <param name="deviceUuid">The UUID of the device.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task CollectAndSendLogsAsync(string deviceUuid)
        {
            _logger.LogInformation("Starting log collection for device {uuid}.", deviceUuid);

            string logFilePath = await _deviceCommunicationService.RequestLogsAsync(deviceUuid);

            if (!string.IsNullOrEmpty(logFilePath))
            {
                _logger.LogInformation("Log file {path} received from device {uuid}.", logFilePath, deviceUuid);

                // Send the log file to the cloud
                await _cloudCommunicationService.SendDeviceLogsAsync(deviceUuid, logFilePath);
            }
            else
            {
                _logger.LogWarning("Failed to collect logs from device {uuid}.", deviceUuid);
            }
        }
    }
}
