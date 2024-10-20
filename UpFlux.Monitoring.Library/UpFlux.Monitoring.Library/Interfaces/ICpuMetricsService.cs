using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Library.Interfaces
{
    /// <summary>
    /// Interface for fetching CPU metrics such as usage and load average.
    /// </summary>
    public interface ICpuMetricsService
    {
        /// <summary>
        /// Fetch the current CPU usage in percentage and load average.
        /// </summary>
        CpuMetrics GetCpuMetrics();
    }
}
