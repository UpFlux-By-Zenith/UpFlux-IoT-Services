using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for collecting logs from devices
    /// </summary>
    public class LogCollectionService
    {
        private readonly ILogger<LogCollectionService> _logger;
        private readonly DeviceCommunicationService _deviceCommunicationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="LogCollectionService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="deviceCommunicationService">Device communication service.</param>
        public LogCollectionService(
            ILogger<LogCollectionService> logger,
            DeviceCommunicationService deviceCommunicationService)
        {
            _logger = logger;
            _deviceCommunicationService = deviceCommunicationService;
        }

        /// <summary>
        /// Initiates log collection from a device and sends the logs to the cloud.
        /// </summary>
        /// <param name="deviceUuid">The UUID of the device.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task<string[]> CollectLogsFromDeviceAsync(string deviceUuid)
        {
            _logger.LogInformation("Starting log collection for device {uuid}.", deviceUuid);

            string[] logFilePaths = await _deviceCommunicationService.RequestLogsAsync(deviceUuid);

            if (logFilePaths != null && logFilePaths.Length > 0)
            {
                foreach (string logFilePath in logFilePaths)
                {
                    _logger.LogInformation("Log file {path} received from device {uuid}.", logFilePath, deviceUuid);
                }
            }
            else
            {
                _logger.LogWarning("Failed to collect logs from device {uuid}.", deviceUuid);
            }

            return logFilePaths;
        }
    }
}
