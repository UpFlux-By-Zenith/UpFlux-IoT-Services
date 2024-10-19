using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Library.Interfaces
{
    /// <summary>
    /// Interface for fetching memory metrics such as total, free, and used memory.
    /// </summary>
    public interface IMemoryMetricsService
    {
        /// <summary>
        /// Fetch the current memory metrics.
        /// </summary>
        MemoryMetrics GetMemoryMetrics();
    }
}
