using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Library.Interfaces
{
    /// <summary>
    /// Interface for fetching system uptime metrics.
    /// </summary>
    public interface ISystemUptimeService
    {
        /// <summary>
        /// Fetch the system uptime in seconds.
        /// </summary>
        SystemUptimeMetrics GetUptime();
    }
}
