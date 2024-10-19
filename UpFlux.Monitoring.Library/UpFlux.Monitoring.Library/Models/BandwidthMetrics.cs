using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Library.Models
{
    /// <summary>
    /// Represents network bandwidth metrics.
    /// </summary>
    public class BandwidthMetrics
    {
        /// <summary>
        /// Download speed in Mbps
        /// </summary>
        public double DownloadSpeed { get; set; }
        /// <summary>
        /// Upload speed in Mbps
        /// </summary>
        public double UploadSpeed { get; set; }
    }
}
