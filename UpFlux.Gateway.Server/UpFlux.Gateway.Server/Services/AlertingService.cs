using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using UpFlux.Gateway.Server.Models;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for monitoring logs and sending alerts to the cloud.
    /// </summary>
    public class AlertingService
    {
        private readonly ILogger<AlertingService> _logger;
        private readonly CloudCommunicationService _cloudCommunicationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="AlertingService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="cloudCommunicationService">Cloud communication service instance.</param>
        public AlertingService(
            ILogger<AlertingService> logger,
            CloudCommunicationService cloudCommunicationService)
        {
            _logger = logger;
            _cloudCommunicationService = cloudCommunicationService;
        }

        /// <summary>
        /// Processes a critical log event and sends an alert to the cloud.
        /// </summary>
        /// <param name="logEvent">The critical log event.</param>
        public async Task ProcessCriticalLogAsync(Models.LogEvent logEvent)
        {
            _logger.LogInformation("Processing critical log event: {message}", logEvent.Message);

            // Create an alert model
            Alert alert = new Alert
            {
                Timestamp = logEvent.Timestamp,
                Level = logEvent.Level,
                Message = logEvent.Message,
                Exception = logEvent.Exception?.ToString(),
                Source = "GatewayServer"
            };

            try
            {
                // Send alert to the cloud
                await _cloudCommunicationService.SendAlertAsync(alert);

                _logger.LogInformation("Alert sent to cloud successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send alert to cloud.");
            }
        }
    }
}
