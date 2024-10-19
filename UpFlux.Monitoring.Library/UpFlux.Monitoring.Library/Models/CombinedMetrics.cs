using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Library.Models
{
    /// <summary>
    /// Represents combined system metrics, including CPU, memory, network, disk, uptime, and temperature.
    /// </summary>
    public class CombinedMetrics
    {
        public CpuMetrics CpuMetrics { get; set; }
        public MemoryMetrics MemoryMetrics { get; set; }
        public NetworkMetrics NetworkMetrics { get; set; }
        public DiskMetrics DiskMetrics { get; set; }
        public SystemUptimeMetrics SystemUptimeMetrics { get; set; }
        public CpuTemperatureMetrics CpuTemperatureMetrics { get; set; }
        public BandwidthMetrics BandwidthMetrics { get; set; }

        /// <summary>
        /// Timestamp of when the metrics were collected.
        /// </summary>
        public DateTime Timestamp { get; set; }
    }
}
