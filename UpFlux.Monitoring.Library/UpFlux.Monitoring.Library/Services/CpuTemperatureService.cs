using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Monitoring.Library.Interfaces;
using UpFlux.Monitoring.Library.Models;
using UpFlux.Monitoring.Library.Utilities;

namespace UpFlux.Monitoring.Library.Services
{
    /// <summary>
    /// Service for fetching CPU temperature.
    /// </summary>
    public class CpuTemperatureService : ICpuTemperatureService
    {
        private const string CpuTempFilePath = "/sys/class/thermal/thermal_zone0/temp";

        /// <summary>
        /// Fetch the current CPU temperature in degrees Celsius.
        /// </summary>
        /// <returns>CpuTemperatureMetrics containing the CPU temperature in Celsius.</returns>
        public CpuTemperatureMetrics GetCpuTemperature()
        {
            try
            {
                // Check if the temperature file exists
                if (!File.Exists(CpuTempFilePath))
                {
                    throw new InvalidOperationException($"Temperature file not found: {CpuTempFilePath}");
                }

                // Read the temperature from the file
                string tempData = LinuxUtility.RunCommand($"cat {CpuTempFilePath}");

                // Convert temperature to Celsius
                if (double.TryParse(tempData, NumberStyles.Float, CultureInfo.InvariantCulture, out double tempMilliCelsius))
                {
                    // divide by 1000 to get the temperature in degrees Celsius
                    double temperatureCelsius = tempMilliCelsius / 1000;

                    return new CpuTemperatureMetrics
                    {
                        TemperatureCelsius = temperatureCelsius
                    };
                }
                else
                {
                    throw new InvalidOperationException("Unable to parse CPU temperature.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error fetching CPU temperature: " + ex.Message, ex);
            }
        }
    }
}
