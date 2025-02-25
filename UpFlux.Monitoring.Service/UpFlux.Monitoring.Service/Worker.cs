using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;

namespace UpFlux.Monitoring.Service
{
    /// <summary>
    /// The background worker service that collects and sends system metrics and sensor data.
    /// Now includes simulation logic to alternate between Busy (send data) and Idle (silent).
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly MetricsCollector _metricsCollector;
        private readonly PythonScriptService _pythonScriptService;
        private readonly TcpClientService _tcpClientService;
        private readonly ServiceSettings _settings;

        // The new simulation state manager
        private readonly SimulationStateManager _stateManager;

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

            // Instantiate the SimulationStateManager so each device has 
            // a unique Busy/Idle pattern
            _stateManager = new SimulationStateManager();
        }

        /// <summary>
        /// Executes the background service.
        /// Depending on Busy or Idle state, we send data or remain silent.
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
                    // Check if license is valid
                    if (IsLicenseValid())
                    {

                        // Determine if Busy or Idle
                        SimulationState currentState = _stateManager.GetCurrentState();

                        if (currentState == SimulationState.Idle)
                        {
                            _logger.LogInformation("Device {uuid} is currently IDLE, no data sent at {time}.", _settings.DeviceUuid, DateTimeOffset.Now);
                            continue;
                        }

                        _logger.LogInformation("License is valid. Proceeding to collect and send data.");

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
                            UUID = _settings.DeviceUuid,
                            Metrics = metrics,
                            SensorData = sensorValues
                        };
                        string jsonData = JsonSerializer.Serialize(combinedData);

                        // Send the data via TCP
                        await _tcpClientService.SendDataAsync(jsonData);

                        _logger.LogInformation("Data sent successfully at: {time}", DateTimeOffset.Now);
                    }
                    else
                    {
                        _logger.LogWarning("License is invalid or expired. Attempting to renew.");

                        // Attempt to renew license
                        _tcpClientService.SendLicenseRenewalRequest();
                    }
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

        /// <summary>
        /// Checks if the license is valid and not expired.
        /// </summary>
        private bool IsLicenseValid()
        {
            try
            {
                string licensePath = _settings.LicenseFilePath;

                if (!File.Exists(licensePath))
                {
                    _logger.LogWarning("License file not found at {path}.", licensePath);
                    return false;
                }

                // Load the XML content
                string licenseContent = File.ReadAllText(licensePath);

                // Parse as XML
                System.Xml.XmlDocument xmlDoc = new System.Xml.XmlDocument();
                xmlDoc.LoadXml(licenseContent);

                // Get the <ExpirationDate> node
                System.Xml.XmlNode? expirationNode = xmlDoc.SelectSingleNode("//Licence/ExpirationDate");
                if (expirationNode == null)
                {
                    _logger.LogWarning("Invalid license file: missing <ExpirationDate> node.");
                    return false;
                }

                // The "o" format is standard ISO 8601 - "2024-10-01T12:00:00Z"
                DateTime expirationDate = DateTime.Parse(
                    expirationNode.InnerText,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind);

                if (expirationDate > DateTime.UtcNow)
                {
                    return true;
                }
                else
                {
                    _logger.LogWarning("License has expired (expired at {0}).", expirationDate);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking license validity.");
                return false;
            }
        }
    }
}
