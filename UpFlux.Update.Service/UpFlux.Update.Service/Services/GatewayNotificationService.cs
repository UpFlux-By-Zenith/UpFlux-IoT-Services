using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UpFlux.Update.Service.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Options;

namespace UpFlux.Update.Service.Services
{
    /// <summary>
    /// Sends logs and notifications to the gateway server.
    /// </summary>
    public class GatewayNotificationService
    {
        private readonly ILogger<GatewayNotificationService> _logger;
        private readonly Configuration _config;
        private readonly HttpClient _httpClient;

        public GatewayNotificationService(ILogger<GatewayNotificationService> logger, IOptions<Configuration> configOptions)
        {
            _logger = logger;
            _config = configOptions.Value;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Sends a log message to the gateway server.
        /// </summary>
        public async Task SendLogAsync(string message)
        {
            var logEntry = new
            {
                Message = message,
                Source = "UpFlux Update Service"
            };

            string jsonContent = JsonConvert.SerializeObject(logEntry);
            StringContent content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await _httpClient.PostAsync(_config.GatewayServerLogEndpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Log sent to gateway server successfully.");
                }
                else
                {
                    _logger.LogWarning("Failed to send log to gateway server. Status Code: " + response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error sending log to gateway server.");
            }
        }
    }
}
