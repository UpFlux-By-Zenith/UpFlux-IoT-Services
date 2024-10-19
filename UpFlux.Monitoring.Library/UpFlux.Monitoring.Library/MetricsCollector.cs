using UpFlux.Monitoring.Library.Interfaces;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Library
{
    /// <summary>
    /// Class responsible for collecting system metrics from various services.
    /// </summary>
    public class MetricsCollector
    {
        private readonly ICpuMetricsService _cpuMetricsService;
        private readonly IMemoryMetricsService _memoryMetricsService;
        private readonly INetworkMetricsService _networkMetricsService;
        private readonly IDiskMetricsService _diskMetricsService;
        private readonly ISystemUptimeService _systemUptimeService;
        private readonly ICpuTemperatureService _cpuTemperatureService;
        private readonly IBandwidthService _bandwidthService;

        /// <summary>
        /// Initializes a new instance of the <see cref="MetricsCollector"/> class with injected services.
        /// </summary>
        /// <param name="cpuMetricsService">Service for CPU metrics.</param>
        /// <param name="memoryMetricsService">Service for memory metrics.</param>
        /// <param name="networkMetricsService">Service for network metrics.</param>
        /// <param name="diskMetricsService">Service for disk metrics.</param>
        /// <param name="systemUptimeService">Service for system uptime metrics.</param>
        /// <param name="cpuTemperatureService">Service for CPU temperature metrics.</param>
        /// <param name="bandwidthService">Service for bandwidth metrics.</param>
        public MetricsCollector(
            ICpuMetricsService cpuMetricsService,
            IMemoryMetricsService memoryMetricsService,
            INetworkMetricsService networkMetricsService,
            IDiskMetricsService diskMetricsService,
            ISystemUptimeService systemUptimeService,
            ICpuTemperatureService cpuTemperatureService,
            IBandwidthService bandwidthService)
        {
            _cpuMetricsService = cpuMetricsService;
            _memoryMetricsService = memoryMetricsService;
            _networkMetricsService = networkMetricsService;
            _diskMetricsService = diskMetricsService;
            _systemUptimeService = systemUptimeService;
            _cpuTemperatureService = cpuTemperatureService;
            _bandwidthService = bandwidthService;
        }

        /// <summary>
        /// Collects and returns all system metrics combined.
        /// </summary>
        /// <returns>A <see cref="CombinedMetrics"/> object containing all collected metrics.</returns>
        public CombinedMetrics CollectAllMetrics()
        {
            try
            {
                CombinedMetrics combinedMetrics = new CombinedMetrics
                {
                    CpuMetrics = _cpuMetricsService.GetCpuMetrics(),
                    MemoryMetrics = _memoryMetricsService.GetMemoryMetrics(),
                    NetworkMetrics = _networkMetricsService.GetNetworkMetrics(),
                    DiskMetrics = _diskMetricsService.GetDiskMetrics(),
                    SystemUptimeMetrics = _systemUptimeService.GetUptime(),
                    CpuTemperatureMetrics = _cpuTemperatureService.GetCpuTemperature(),
                    BandwidthMetrics = _bandwidthService.GetBandwidthMetrics(),
                    Timestamp = DateTime.UtcNow
                };

                return combinedMetrics;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error collecting metrics: " + ex.Message, ex);
            }
        }
    }
}
