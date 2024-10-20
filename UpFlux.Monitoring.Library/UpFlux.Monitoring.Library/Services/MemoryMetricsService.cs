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
    /// Service for fetching memory metrics such as total, free, and used memory.
    /// </summary>
    public class MemoryMetricsService : IMemoryMetricsService
    {
        /// <summary>
        /// Fetch the current memory metrics such as total, free, and used memory.
        /// </summary>
        /// <returns>MemoryMetrics containing total, free, and used memory in bytes.</returns>
        public MemoryMetrics GetMemoryMetrics()
        {
            try
            {
                // Read memory data from /proc/meminfo
                string memInfoData = LinuxUtility.RunCommand("cat /proc/meminfo");

                MemoryMetrics metrics = ParseMemoryMetrics(memInfoData);

                return metrics;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error fetching memory metrics: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Parses the memory information from the /proc/meminfo file.
        /// </summary>
        /// <param name="memInfoOutput">The output from the /proc/meminfo file.</param>
        /// <returns>MemoryMetrics object with total, free, and used memory in bytes.</returns>
        private MemoryMetrics ParseMemoryMetrics(string memInfoOutput)
        {
            try
            {
                // Split the output by lines and parse relevant lines
                string[] lines = memInfoOutput.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                long totalMemory = 0;
                long freeMemory = 0;

                foreach (string line in lines)
                {
                    if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                    {
                        totalMemory = ParseMemoryValue(line);
                    }
                    else if (line.StartsWith("MemFree:", StringComparison.OrdinalIgnoreCase))
                    {
                        freeMemory = ParseMemoryValue(line);
                    }

                    // To get only need the total and free memory values
                    if (totalMemory > 0 && freeMemory > 0)
                    {
                        break;
                    }
                }

                // Check if both values were parsed
                if (totalMemory == 0 || freeMemory == 0)
                {
                    throw new InvalidOperationException("Failed to retrieve valid memory metrics.");
                }

                return new MemoryMetrics
                {
                    TotalMemory = totalMemory,
                    FreeMemory = freeMemory
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error parsing memory metrics: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Parses a memory value line ("MemTotal: 16392628 kB") and converts the value to bytes.
        /// </summary>
        /// <param name="line">The line from the /proc/meminfo file.</param>
        /// <returns>The memory value in bytes.</returns>
        private long ParseMemoryValue(string line)
        {
            try
            {
                // Split the line by spaces and get the second item (the memory value in KB)
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long memoryInKb))
                {
                    // Convert from kilobytes to bytes 1KB = 1024 bytes
                    return memoryInKb * 1024;
                }
                else
                {
                    throw new InvalidOperationException("Unable to parse memory value from line: " + line);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error parsing memory value: " + ex.Message, ex);
            }
        }
    }
}
