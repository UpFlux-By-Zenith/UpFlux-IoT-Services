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
                string[] headers = GetNetworkHeaders();
                string[] networkData = GetNetworkInterfaceData(_networkInterface);

                if (networkData != null && headers != null)
                {
                    int receivedIndex = Array.IndexOf(headers, "bytes");
                    int transmittedIndex = Array.LastIndexOf(headers, "bytes");

                    if (receivedIndex == -1 || transmittedIndex == -1)
                    {
                        throw new InvalidOperationException("Unable to determine columns for received or transmitted bytes.");
                    }

                    // Parse received and transmitted bytes using dynamic column indices
                    if (long.TryParse(networkData[receivedIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out long receivedBytes) &&
                        long.TryParse(networkData[transmittedIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out long transmittedBytes))
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
        /// Retrieves the headers from /proc/net/dev to dynamically find the column indices.
        /// </summary>
        /// <returns>An array of headers representing the columns.</returns>
        private string[] GetNetworkHeaders()
        {
            try
            {
                string[] lines = LinuxUtility.RunCommand("cat /proc/net/dev").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string? headerLine = lines.FirstOrDefault(line => line.TrimStart().StartsWith("Inter-"));
                if (headerLine != null)
                {
                    string[] headers = headerLine.Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries);
                    return headers;
                }
                throw new InvalidOperationException("Unable to read network headers from /proc/net/dev.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error reading /proc/net/dev headers: " + ex.Message, ex);
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
                string[] lines = File.ReadAllLines("/proc/net/dev");

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
