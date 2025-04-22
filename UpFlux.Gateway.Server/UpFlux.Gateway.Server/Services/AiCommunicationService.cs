using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Gateway.Server.Models;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Calls the Python AI microservice to:
    ///   1) Perform DBSCAN clustering
    ///   2) Perform scheduling Heuristic + LNS - Large Neighborhood Search approach
    /// </summary>
    public class AiCommunicationService
    {
        private readonly ILogger<AiCommunicationService> _logger;
        private readonly DeviceUsageAggregator _usageAggregator;
        private readonly GatewaySettings _settings;
        private readonly HttpClient _httpClient;

        public AiCommunicationService(
            ILogger<AiCommunicationService> logger,
            IOptions<GatewaySettings> options,
            DeviceUsageAggregator usageAggregator
        )
        {
            _logger = logger;
            _usageAggregator = usageAggregator;
            _settings = options.Value;
            _httpClient = new HttpClient(); // base HttpClient
        }

        /// <summary>
        /// Gathers usage vectors from the aggregator
        /// Calls the Python AI for DBSCAN, returning cluster info + 2D coords for plotting for the UI.
        /// </summary>
        public async Task<AiClusteringResult> RunClusteringAsync()
        {
            List<DeviceUsageVector> vectors = _usageAggregator.ComputeUsageVectors();
            if (vectors.Count == 0)
            {
                _logger.LogWarning("No device usage data to cluster. Possibly no active devices.");
                return null;
            }

            string aiUrl = $"{_settings.AiServiceAddress}/ai/clustering";

            try
            {
                HttpResponseMessage resp = await _httpClient.PostAsJsonAsync(aiUrl, vectors);
                resp.EnsureSuccessStatusCode();

                AiClusteringResult result = await resp.Content.ReadFromJsonAsync<AiClusteringResult>();
                // log the full json file for debugging
                string json = await resp.Content.ReadAsStringAsync();
                _logger.LogInformation("AI clustering returned {0} clusters", result?.Clusters?.Count ?? 0);
                _logger.LogInformation("AI clustering returned {0} plot points", result?.PlotData?.Count ?? 0);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling AI /ai/clustering");
                return null;
            }
        }

        /// <summary>
        /// Calls /ai/scheduling, providing cluster info, to get recommended update times.
        /// </summary>
        public async Task<AiSchedulingResult> RunSchedulingAsync(AiClusteringResult clustering, object aggregatorDataList)
        {
            if (clustering == null || clustering.Clusters == null || clustering.Clusters.Count == 0)
            {
                _logger.LogWarning("No clusters found, skipping scheduling.");
                return null;
            }

            var payload = new
            {
                clusters = clustering.Clusters,
                plotData = clustering.PlotData,
                aggregatorData = aggregatorDataList
            };

            string aiUrl = $"{_settings.AiServiceAddress}/ai/scheduling";

            try
            {
                HttpResponseMessage resp = await _httpClient.PostAsJsonAsync(aiUrl, payload);
                resp.EnsureSuccessStatusCode();

                AiSchedulingResult schedule = await resp.Content.ReadFromJsonAsync<AiSchedulingResult>();
                _logger.LogInformation("AI scheduling returned recommended times for {0} clusters.",
                    schedule?.Clusters?.Count ?? 0);
                return schedule;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling AI /ai/scheduling");
                return null;
            }
        }
    }

    // Models for receiving AI results

    public class AiClusteringResult
    {
        public List<AiCluster> Clusters { get; set; }
        public List<AiPlotPoint> PlotData { get; set; }
    }

    public class AiCluster
    {
        public string ClusterId { get; set; }
        public List<string> DeviceUuids { get; set; }
    }

    public class AiPlotPoint
    {
        public string DeviceUuid { get; set; }
        public double X { get; set; }  // coordinate for cluster graph for the Ui
        public double Y { get; set; }  // coordinate for cluster graph for the Ui
        public string ClusterId { get; set; }
        public bool IsSynthetic { get; set; }
    }

    public class AiSchedulingResult
    {
        public List<AiScheduledCluster> Clusters { get; set; }
    }

    public class AiScheduledCluster
    {
        public string ClusterId { get; set; }
        public List<string> DeviceUuids { get; set; }
        public DateTime UpdateTimeUtc { get; set; } // recommended update time
    }
}
