using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Repositories;
using UpFlux.Gateway.Server.Services;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for aggregating monitoring data from devices.
    /// </summary>
    public class DataAggregationService
    {
        private readonly ILogger<DataAggregationService> _logger;
        private readonly GatewaySettings _settings;
        private readonly ConcurrentDictionary<string, List<MonitoringData>> _deviceData;
        private readonly Timer _aggregationTimer;
        private readonly CloudCommunicationService _cloudCommunicationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataAggregationService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="settings">Gateway settings.</param>
        /// <param name="cloudCommunicationService">Service for communication with the cloud.</param>
        public DataAggregationService(
            ILogger<DataAggregationService> logger,
            IOptions<GatewaySettings> settings,
            CloudCommunicationService cloudCommunicationService)
        {
            _logger = logger;
            _settings = settings.Value;
            _cloudCommunicationService = cloudCommunicationService;

            _deviceData = new ConcurrentDictionary<string, List<MonitoringData>>();

            // Start the aggregation timer
            _aggregationTimer = new Timer(
                AggregateAndForwardData,
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(_settings.DataAggregationIntervalSeconds));
        }

        /// <summary>
        /// Adds monitoring data received from a device for aggregation.
        /// </summary>
        /// <param name="data">The monitoring data to add.</param>
        public void AddMonitoringData(MonitoringData data)
        {
            try
            {
                if (!_deviceData.ContainsKey(data.UUID))
                {
                    _deviceData[data.UUID] = new List<MonitoringData>();
                }

                _deviceData[data.UUID].Add(data);
                _logger.LogInformation("Added monitoring data for device UUID: {uuid}", data.UUID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding monitoring data for device UUID: {uuid}", data.UUID);
            }
        }

        /// <summary>
        /// Aggregates data from devices and forwards it to the cloud.
        /// </summary>
        /// <param name="state">The state object (not used).</param>
        private void AggregateAndForwardData(object state)
        {
            _logger.LogInformation("Starting data aggregation and forwarding.");

            try
            {
                List<AggregatedData> aggregatedData = new List<AggregatedData>();

                foreach (var deviceEntry in _deviceData)
                {
                    string uuid = deviceEntry.Key;
                    List<MonitoringData> dataList = deviceEntry.Value;

                    if (dataList.Count == 0)
                        continue;

                    // Aggregate data for the device
                    Metrics aggregatedMetrics = new Metrics
                    {
                        CpuUsage = dataList.Average(d => d.Metrics.CpuUsage),
                        MemoryUsage = dataList.Average(d => d.Metrics.MemoryUsage),
                        DiskUsage = dataList.Average(d => d.Metrics.DiskUsage),
                        CpuTemperature = dataList.Average(d => d.Metrics.CpuTemperature),
                        SystemUptime = dataList.Max(d => d.Metrics.SystemUptime),
                        NetworkUsage = new NetworkUsage
                        {
                            BytesSent = dataList.Sum(d => d.Metrics.NetworkUsage.BytesSent),
                            BytesReceived = dataList.Sum(d => d.Metrics.NetworkUsage.BytesReceived)
                        }
                    };

                    SensorData aggregatedSensorData = new SensorData
                    {
                        RedValue = (int)dataList.Average(d => d.SensorData.RedValue),
                        GreenValue = (int)dataList.Average(d => d.SensorData.GreenValue),
                        BlueValue = (int)dataList.Average(d => d.SensorData.BlueValue)
                    };

                    AggregatedData aggregatedEntry = new AggregatedData
                    {
                        UUID = uuid,
                        Timestamp = DateTime.UtcNow,
                        Metrics = aggregatedMetrics,
                        SensorData = aggregatedSensorData
                    };

                    aggregatedData.Add(aggregatedEntry);

                    // Clear the data list for the device after aggregation
                    dataList.Clear();
                }

                if (aggregatedData.Count > 0)
                {
                    // Forward the aggregated data to the cloud
                    _cloudCommunicationService.SendAggregatedDataAsync(aggregatedData)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                }
                else
                {
                    _logger.LogInformation("No data to aggregate at this time.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during data aggregation and forwarding.");
            }
        }
    }
}
