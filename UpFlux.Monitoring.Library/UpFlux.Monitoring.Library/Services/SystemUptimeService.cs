using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Monitoring.Library.Interfaces;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Library.Services
{
    /// <summary>
    /// Service for fetching system uptime metrics.
    /// </summary>
    public class SystemUptimeService : ISystemUptimeService
    {
        /// <summary>
        /// Fetch the system uptime in seconds.
        /// </summary>
        /// <returns>SystemUptimeMetrics containing the system uptime in seconds.</returns>
        public SystemUptimeMetrics GetUptime()
        {
            try
            {
                // Read the uptime data from /proc/uptime
                string uptimeData = File.ReadAllText("/proc/uptime");

                // The first value the uptime of the system(including time spent in suspend)
                string[] uptimeValues = uptimeData.Split(' ');

                if (double.TryParse(uptimeValues[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double uptimeSeconds))
                {
                    return new SystemUptimeMetrics
                    {
                        UptimeSeconds = (long)Math.Floor(uptimeSeconds)
                    };
                }
                else
                {
                    throw new InvalidOperationException("Unable to parse system uptime.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error fetching system uptime: " + ex.Message, ex);
            }
        }
    }
}
