using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Library.Interfaces
{
    /// <summary>
    /// Interface for fetching network metrics such as received and transmitted bytes.
    /// </summary>
    public interface INetworkMetricsService
    {
        /// <summary>
        /// Fetch the current network metrics.
        /// </summary>
        NetworkMetrics GetNetworkMetrics();
    }
}
