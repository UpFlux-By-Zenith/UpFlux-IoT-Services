using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Library.Models
{
    /// <summary>
    /// Represents network metrics such as received and transmitted data.
    /// </summary>
    public class NetworkMetrics
    {
        /// <summary>
        /// Bytes received over the network
        /// </summary>
        public long ReceivedBytes { get; set; }
        /// <summary>
        /// Bytes transmitted over the network
        /// </summary>
        public long TransmittedBytes { get; set; }
    }
}
