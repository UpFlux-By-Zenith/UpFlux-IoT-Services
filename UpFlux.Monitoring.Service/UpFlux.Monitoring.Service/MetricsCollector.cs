using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UpFlux.Monitoring.Library.Interfaces;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Service
{
    /// <summary>
    /// Collects system metrics using the UpFlux Monitoring Library.
    /// </summary>
    public class MetricsCollector
    {
        private readonly ICpuMetricsService _cpuMetricsService;
        private readonly IMemoryMetricsService _memoryMetricsService;
        private readonly INetworkMetricsService _networkMetricsService;
        private readonly IDiskMetricsService _diskMetricsService;
        private readonly ISystemUptimeService _systemUptimeService;
        private readonly ICpuTemperatureService _cpuTemperatureService;
        private readonly ILogger<MetricsCollector> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MetricsCollector"/> class.
        /// </summary>
        public MetricsCollector(
            ICpuMetricsService cpuMetricsService,
            IMemoryMetricsService memoryMetricsService,
            INetworkMetricsService networkMetricsService,
            IDiskMetricsService diskMetricsService,
            ISystemUptimeService systemUptimeService,
            ICpuTemperatureService cpuTemperatureService,
            ILogger<MetricsCollector> logger)
        {
            _cpuMetricsService = cpuMetricsService;
            _memoryMetricsService = memoryMetricsService;
            _networkMetricsService = networkMetricsService;
            _diskMetricsService = diskMetricsService;
            _systemUptimeService = systemUptimeService;
            _cpuTemperatureService = cpuTemperatureService;
            _logger = logger;
        }

        /// <summary>
        /// Collects all system metrics and returns a combined metrics object.
        /// </summary>
        public CombinedMetrics CollectAllMetrics()
        {
            try
            {
                _logger.LogInformation("Collecting CPU metrics...");
                CpuMetrics cpuMetrics = _cpuMetricsService.GetCpuMetrics();

                _logger.LogInformation("Collecting memory metrics...");
                MemoryMetrics memoryMetrics = _memoryMetricsService.GetMemoryMetrics();

                _logger.LogInformation("Collecting network metrics...");
                NetworkMetrics networkMetrics = _networkMetricsService.GetNetworkMetrics();

                _logger.LogInformation("Collecting disk metrics...");
                DiskMetrics diskMetrics = _diskMetricsService.GetDiskMetrics();

                _logger.LogInformation("Collecting system uptime metrics...");
                SystemUptimeMetrics uptimeMetrics = _systemUptimeService.GetUptime();

                _logger.LogInformation("Collecting CPU temperature metrics...");
                CpuTemperatureMetrics cpuTemperatureMetrics = _cpuTemperatureService.GetCpuTemperature();

                CombinedMetrics combinedMetrics = new CombinedMetrics
                {
                    CpuMetrics = cpuMetrics,
                    MemoryMetrics = memoryMetrics,
                    NetworkMetrics = networkMetrics,
                    DiskMetrics = diskMetrics,
                    SystemUptimeMetrics = uptimeMetrics,
                    CpuTemperatureMetrics = cpuTemperatureMetrics,
                    Timestamp = DateTime.UtcNow
                };

                _logger.LogInformation("System metrics collected successfully.");

                return combinedMetrics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting system metrics.");
                throw;
            }
        }
    }
}
