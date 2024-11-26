using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents the aggregated data ready to be sent to the cloud.
    /// </summary>
    public class AggregatedData
    {
        /// <summary>
        /// Gets or sets the device UUID.
        /// </summary>
        public string UUID { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the aggregation (UTC).
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the aggregated metrics data.
        /// </summary>
        public Metrics Metrics { get; set; }

        /// <summary>
        /// Gets or sets the aggregated sensor data.
        /// </summary>
        public SensorData SensorData { get; set; }
    }
}
