using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Library.Models
{
    /// <summary>
    /// Represents system uptime in seconds.
    /// </summary>
    public class SystemUptimeMetrics
    {
        /// <summary>
        /// System uptime in seconds
        /// </summary>
        public long UptimeSeconds { get; set; }
    }
}
