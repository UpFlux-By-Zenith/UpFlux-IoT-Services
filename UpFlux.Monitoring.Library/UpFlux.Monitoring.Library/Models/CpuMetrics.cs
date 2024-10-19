using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Library.Models
{
    /// <summary>
    /// Represents the CPU metrics such as current usage and load average.
    /// </summary>
    public class CpuMetrics
    {
        /// <summary>
        /// CPU usage in percentage
        /// </summary>
        public double CurrentUsage { get; set; }
        /// <summary>
        /// Load average over a given time period
        /// </summary>
        public double LoadAverage { get; set; }
    }
}
