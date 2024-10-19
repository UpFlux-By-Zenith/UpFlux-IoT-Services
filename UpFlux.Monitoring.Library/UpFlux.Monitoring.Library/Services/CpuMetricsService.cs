using System;
using System.Globalization;
using System.Linq;
using UpFlux.Monitoring.Library.Interfaces;
using UpFlux.Monitoring.Library.Models;
using UpFlux.Monitoring.Library.Utilities;

namespace UpFlux.Monitoring.Library.Services
{
    /// <summary>
    /// Service for fetching CPU metrics such as usage and load average.
    /// </summary>
    public class CpuMetricsService : ICpuMetricsService
    {
        /// <summary>
        /// Fetch the current CPU usage in percentage and load average.
        /// </summary>
        /// <returns>CpuMetrics containing CPU usage and load average.</returns>
        public CpuMetrics GetCpuMetrics()
        {
            double cpuUsage = GetCpuUsage();
            double loadAverage = GetLoadAverage();

            return new CpuMetrics
            {
                CurrentUsage = cpuUsage,
                LoadAverage = loadAverage
            };
        }

        /// <summary>
        /// Gets the current CPU usage percentage using the mpstat command.
        /// </summary>
        /// <returns>CPU usage as a percentage.</returns>
        private double GetCpuUsage()
        {
            try
            {
                // Run mpstat to get the CPU stats output
                string command = "mpstat | grep 'all'";
                string result = LinuxUtility.RunCommand(command);

                // Split the result into words
                string[] columns = result.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                // Find the index of the '%idle' column
                int idleIndex = Array.FindIndex(columns, col => col.Contains("%idle", StringComparison.OrdinalIgnoreCase));

                if (idleIndex == -1 || idleIndex >= columns.Length)
                {
                    throw new InvalidOperationException("Unable to find the '%idle' column.");
                }

                // Get the idle percentage from the column
                if (double.TryParse(columns[idleIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double idlePercentage))
                {
                    // CPU usage is 100% - idle percentage
                    return 100 - idlePercentage;
                }
                else
                {
                    throw new InvalidOperationException("Unable to parse CPU idle percentage.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error fetching CPU usage: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Gets the load average from the /proc/loadavg file.
        /// </summary>
        /// <returns>Load average over 1 minute.</returns>
        private double GetLoadAverage()
        {
            try
            {
                // Read the load average from the /proc/loadavg file
                string loadAvgData = File.ReadAllText("/proc/loadavg");

                // The first value is the 1 minute load average
                string[] loadAvgValues = loadAvgData.Split(' ');

                if (double.TryParse(loadAvgValues[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double loadAverage))
                {
                    return loadAverage;
                }
                else
                {
                    throw new InvalidOperationException("Unable to parse load average.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error fetching load average: " + ex.Message, ex);
            }
        }
    }
}
