using System;
using System.Globalization;
using UpFlux.Monitoring.Library.Interfaces;
using UpFlux.Monitoring.Library.Models;
using UpFlux.Monitoring.Library.Utilities;

namespace UpFlux.Monitoring.Library.Services
{
    /// <summary>
    /// Service for fetching network metrics such as received and transmitted bytes.
    /// </summary>
    public class NetworkMetricsService : INetworkMetricsService
    {
        private readonly string _networkInterface;

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkMetricsService"/> class.
        /// Uses the primary network interface if none is provided.
        /// </summary>
        /// <param name="networkInterface">The name of the network interface</param>
        public NetworkMetricsService(string networkInterface = null)
        {
            _networkInterface = networkInterface ?? NetworkInterfaceDetector.GetPrimaryNetworkInterface();
        }

        /// <summary>
        /// Fetches the current network metrics such as received and transmitted bytes.
        /// </summary>
        /// <returns>NetworkMetrics containing received and transmitted bytes.</returns>
        public NetworkMetrics GetNetworkMetrics()
        {
            try
            {
                string[] networkData = GetNetworkInterfaceData(_networkInterface);

                if (networkData != null)
                {
                    // Received bytes are the first value after the network interface
                    // columns are hard-coded based on the output of /proc/net/dev
                    if (long.TryParse(networkData[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long receivedBytes) &&
                        long.TryParse(networkData[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out long transmittedBytes))
                    {
                        return new NetworkMetrics
                        {
                            ReceivedBytes = receivedBytes,
                            TransmittedBytes = transmittedBytes
                        };
                    }
                    else
                    {
                        throw new InvalidOperationException("Unable to parse network metrics.");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Could not retrieve metrics for network interface {_networkInterface}.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error fetching network metrics: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Parses the /proc/net/dev file to get data for a specific network interface.
        /// </summary>
        /// <param name="networkInterface">The network interface name</param>
        /// <returns>Array of strings representing the data for the specified network interface.</returns>
        private string[] GetNetworkInterfaceData(string networkInterface)
        {
            try
            {
                string[] lines = LinuxUtility.RunCommand("cat /proc/net/dev").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                // Look for the line that starts with the specified network interface
                foreach (string line in lines)
                {
                    if (line.TrimStart().StartsWith(networkInterface + ":"))
                    {
                        // Split the line into parts
                        string[] data = line.Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                        return data;
                    }
                }

                throw new InvalidOperationException($"Network interface '{networkInterface}' not found.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error reading /proc/net/dev: " + ex.Message, ex);
            }
        }
    }
}
