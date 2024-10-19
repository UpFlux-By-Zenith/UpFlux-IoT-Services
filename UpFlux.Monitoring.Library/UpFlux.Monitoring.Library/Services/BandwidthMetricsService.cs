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
    /// Service for fetching network bandwidth metrics (download and upload speeds).
    /// </summary>
    public class BandwidthMetricsService : IBandwidthService
    {
        /// <summary>
        /// Fetch the current network bandwidth metrics for a given interface.
        /// </summary>
        /// <param name="networkInterface">The network interface to gather metrics from if provided</param>
        /// <returns>BandwidthMetrics containing download and upload speeds.</returns>
        public BandwidthMetrics GetBandwidthMetrics(string networkInterface = null)
        {
            // If no interface is provided then auto-detect
            if (string.IsNullOrWhiteSpace(networkInterface))
            {
                networkInterface = NetworkInterfaceDetector.GetPrimaryNetworkInterface();
                if (string.IsNullOrEmpty(networkInterface))
                {
                    throw new InvalidOperationException("No network interface provided and primary interface could not be detected.");
                }
            }

            BandwidthMetrics bandwidthMetrics = new BandwidthMetrics
            {
                DownloadSpeed = GetDownloadSpeed(networkInterface),
                UploadSpeed = GetUploadSpeed(networkInterface)
            };
            return bandwidthMetrics;
        }


        /// <summary>
        /// Gets the current download speed in Kbps using the ifstat command.
        /// </summary>
        /// <param name="networkInterface">The network interface to gather metrics from.</param>
        /// <returns>Download speed in Kbps.</returns>
        private double GetDownloadSpeed(string networkInterface)
        {
            try
            {
                string command = $"ifstat -i {networkInterface} 1 1 | awk 'NR==3 {{print $1}}'";
                string result = LinuxUtility.RunCommand(command);

                if (double.TryParse(result.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double downloadSpeedKbps))
                {
                    return downloadSpeedKbps;
                }
                else
                {
                    throw new InvalidOperationException("Unable to parse download speed.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error fetching download speed: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Gets the current upload speed in Kbps using the ifstat command.
        /// </summary>
        /// <param name="networkInterface">The network interface to gather metrics from.</param>
        /// <returns>Upload speed in Kbps.</returns>
        private double GetUploadSpeed(string networkInterface)
        {
            try
            {
                string command = $"ifstat -i {networkInterface} 1 1 | awk 'NR==3 {{print $2}}'";
                string result = LinuxUtility.RunCommand(command);

                if (double.TryParse(result.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double uploadSpeedKbps))
                {
                    return uploadSpeedKbps;
                }
                else
                {
                    throw new InvalidOperationException("Unable to parse upload speed.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error fetching upload speed: " + ex.Message, ex);
            }
        }
    }
}
