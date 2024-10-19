using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Library.Models
{
    /// <summary>
    /// Represents CPU temperature in degrees Celsius.
    /// </summary>
    public class CpuTemperatureMetrics
    {
        /// <summary>
        /// CPU temperature in degrees Celsius
        /// </summary>
        public double TemperatureCelsius { get; set; }
    }
}
