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
    /// Service for fetching disk metrics such as total, free, and used space.
    /// </summary>
    public class DiskMetricsService : IDiskMetricsService
    {
        /// <summary>
        /// Fetch the current disk metrics such as total, free, and used space.
        /// </summary>
        /// <returns>DiskMetrics containing total, free, and used space in bytes.</returns>
        public DiskMetrics GetDiskMetrics()
        {
            try
            {
                // Run the df command to get the disk space usage for the root file system
                string command = "df / | grep '/'";
                string result = LinuxUtility.RunCommand(command);

                // Parse the df command result
                var metrics = ParseDiskMetrics(result);

                return metrics;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error fetching disk metrics: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Parses the result of the df command to extract disk metrics.
        /// </summary>
        /// <param name="dfOutput">Output of the df command.</param>
        /// <returns>DiskMetrics object with total, free, and used space in bytes.</returns>
        private DiskMetrics ParseDiskMetrics(string dfOutput)
        {
            try
            {
                // Split the df output by spaces and filter out empty results
                string[] parts = dfOutput.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 6)
                {
                    throw new InvalidOperationException("Unexpected format in df command output.");
                }

                // Get the 1K-blocks, Used, Available columns
                if (long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long totalSpaceKb) &&
                    long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long freeSpaceKb))
                {
                    // Convert from kilobytes to bytes 1KB = 1024 bytes
                    long totalSpaceBytes = totalSpaceKb * 1024;
                    long freeSpaceBytes = freeSpaceKb * 1024;

                    return new DiskMetrics
                    {
                        TotalDiskSpace = totalSpaceBytes,
                        FreeDiskSpace = freeSpaceBytes
                    };
                }
                else
                {
                    throw new InvalidOperationException("Unable to parse disk metrics.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error parsing disk metrics: " + ex.Message, ex);
            }
        }
    }
}
