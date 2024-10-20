using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Library.Interfaces
{
    /// <summary>
    /// Interface for fetching CPU temperature.
    /// </summary>
    public interface ICpuTemperatureService
    {
        /// <summary>
        /// Fetch the current CPU temperature in degrees Celsius.
        /// </summary>
        CpuTemperatureMetrics GetCpuTemperature();
    }
}
