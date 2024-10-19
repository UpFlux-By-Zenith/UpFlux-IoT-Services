using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Library.Interfaces
{
    /// <summary>
    /// Interface for fetching disk metrics such as total, free, and used space.
    /// </summary>
    public interface IDiskMetricsService
    {
        /// <summary>
        /// Fetch the current disk metrics.
        /// </summary>
        DiskMetrics GetDiskMetrics();
    }
}
