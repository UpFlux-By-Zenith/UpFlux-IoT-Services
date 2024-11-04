using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UpFlux.Monitoring.Service
{
    /// <summary>
    /// The background worker service that collects and sends system metrics and sensor data.
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly MetricsCollector _metricsCollector;
        private readonly PythonScriptService _pythonScriptService;
        private readonly TcpClientService _tcpClientService;
        private readonly ServiceSettings _settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="Worker"/> class.
        /// </summary>
        public Worker(
            ILogger<Worker> logger,
            MetricsCollector metricsCollector,
            PythonScriptService pythonScriptService,
            TcpClientService tcpClientService,
            IOptions<ServiceSettings> settings)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _pythonScriptService = pythonScriptService;
            _tcpClientService = tcpClientService;
            _settings = settings.Value;
        }

        /// <summary>
        /// Executes the background service.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker service starting...");

            // Start the Python sensor script
            _pythonScriptService.StartPythonScript();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Collecting system metrics...");

                    // Collect system metrics
                    var metrics = _metricsCollector.CollectAllMetrics();

                    // Get the latest sensor data
                    string sensorData = _pythonScriptService.GetLatestSensorData();

                    SensorData sensorValues = null;
                    if (!string.IsNullOrEmpty(sensorData))
                    {
                        try
                        {
                            sensorValues = JsonSerializer.Deserialize<SensorData>(sensorData);
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "Failed to deserialize sensor data.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No sensor data received.");
                    }

                    // Combine the data into one JSON object
                    var combinedData = new
                    {
                        Metrics = metrics,
                        SensorData = sensorValues
                    };
                    string jsonData = JsonSerializer.Serialize(combinedData);

                    // Send the data via TCP
                    _tcpClientService.SendData(jsonData);

                    _logger.LogInformation("Data: {data}", jsonData);

                    _logger.LogInformation("Data sent successfully at: {time}", DateTimeOffset.Now);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during data collection and transmission.");
                }

                // Wait for the specified interval before collecting data again
                await Task.Delay(_settings.MonitoringIntervalSeconds * 1000, stoppingToken);
            }

            // Stop the Python sensor script when the service is stopping
            _pythonScriptService.StopPythonScript();

            _logger.LogInformation("Worker service stopping...");
        }
    }
}

