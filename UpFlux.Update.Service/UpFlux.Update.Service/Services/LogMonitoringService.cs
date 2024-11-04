using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Extensions.Logging;
using UpFlux.Update.Service.Models;
using Microsoft.Extensions.Options;

namespace UpFlux.Update.Service.Services
{
    /// <summary>
    /// Monitors the logs of the UpFlux Monitoring Service after an update.
    /// </summary>
    public class LogMonitoringService
    {
        private readonly ILogger<LogMonitoringService> _logger;
        private readonly Configuration _config;
        private DateTime _startTime;

        public LogMonitoringService(ILogger<LogMonitoringService> logger, IOptions<Configuration> configOptions)
        {
            _logger = logger;
            _config = configOptions.Value;
        }

        /// <summary>
        /// Monitors the logs for errors after installation.
        /// </summary>
        public async Task<bool> MonitorLogsAsync()
        {
            _logger.LogInformation("Starting post-installation log monitoring.");
            _startTime = DateTime.Now;

            DateTime endTime = _startTime.AddMinutes(_config.PostInstallationMonitoringMinutes);
            bool errorDetected = false;

            while (DateTime.Now <= endTime)
            {
                if (CheckForErrors())
                {
                    errorDetected = true;
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(30));
            }

            return !errorDetected;
        }

        private bool CheckForErrors()
        {
            try
            {
                string[] logLines = File.ReadAllLines(_config.MonitoringServiceLog);
                foreach (string line in logLines)
                {
                    if (DateTime.TryParse(line.Substring(0, 19), out DateTime logTime))
                    {
                        if (logTime >= _startTime)
                        {
                            foreach (var pattern in _config.ErrorPatterns)
                            {
                                if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger.LogError($"Error detected in monitoring service log: {line}");
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading monitoring service log.");
            }

            return false;
        }
    }
}

