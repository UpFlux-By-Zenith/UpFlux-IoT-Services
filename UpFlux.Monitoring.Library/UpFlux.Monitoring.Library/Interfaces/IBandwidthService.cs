using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Library.Interfaces
{
    /// <summary>
    /// Interface for fetching network bandwidth metrics (download and upload speeds).
    /// </summary>
    public interface IBandwidthService
    {
        /// <summary>
        /// Fetch the current network bandwidth metrics.
        /// </summary>
        BandwidthMetrics GetBandwidthMetrics(string networkInterface);
    }
}
