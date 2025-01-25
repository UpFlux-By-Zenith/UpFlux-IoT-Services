using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Events;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Protos;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for monitoring logs and sending alerts to the cloud.
    /// </summary>
    public class AlertingService
    {
        private readonly ILogger<AlertingService> _logger;
        private readonly GatewaySettings _gatewaySettings;

        public event Func<AlertMessage, Task> OnAlertGenerated;

        /// <summary>
        /// Initializes a new instance of the <see cref="AlertingService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="gatewaySettings">Gateway settings (via IOptions).</param>
        public AlertingService(
            ILogger<AlertingService> logger,
            IOptions<GatewaySettings> gatewaySettings)
        {
            _logger = logger;
            _gatewaySettings = gatewaySettings.Value;

        }

        /// <summary>
        /// Processes a critical log event and sends an alert to the cloud.
        /// </summary>
        /// <param name="logEvent">The critical log event.</param>
        public async Task ProcessCriticalLogAsync(Models.LogEvent logEvent)
        {
            _logger.LogInformation("Processing critical log event: {message}", logEvent.Message);

            // Create an alert model
            AlertMessage alert = new AlertMessage
            {
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(logEvent.Timestamp),
                Level = logEvent.Level,
                Message = logEvent.Message,
                Exception = logEvent.Exception?.ToString(),
                Source = _gatewaySettings.GatewayId,
            };

            if (OnAlertGenerated != null)
            {
                await OnAlertGenerated.Invoke(alert);
            }
        }

        /// <summary>
        /// Processes a device log event and sends it to the cloud as an alert.
        /// </summary>
        /// <param name="logEvent">The device log event.</param>
        public async Task ProcessDeviceLogAsync(Alert alert)
        {
            _logger.LogInformation("Processing device log event: {message}", alert.Message);

            try
            {
                AlertMessage alertMessage = new AlertMessage
                {
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(alert.Timestamp),
                    Level = alert.Level,
                    Message = alert.Message,
                    Exception = alert.Exception,
                    Source = _gatewaySettings.GatewayId,
                };

                if (OnAlertGenerated != null)
                {
                    await OnAlertGenerated.Invoke(alertMessage);
                }

                _logger.LogInformation("Device log sent to cloud successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send device log to cloud.");
            }
        }
    }
}
