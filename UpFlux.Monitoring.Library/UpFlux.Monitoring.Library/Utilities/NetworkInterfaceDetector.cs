using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Library.Utilities
{
    /// <summary>
    /// Represents a utility for detecting the primary network interface.
    /// </summary>
    public static class NetworkInterfaceDetector
    {
        /// <summary>
        /// Detects the primary network interface by using the default route.
        /// </summary>
        /// <returns>The name of the primary network interface, or null if not found.</returns>
        public static string GetPrimaryNetworkInterface()
        {
            try
            {
                // Use `ip route` command to get the default network interface
                string command = "ip route | grep default | awk '{print $5}'";
                string result = LinuxUtility.RunCommand(command);

                return result.Trim();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error detecting primary network interface: " + ex.Message, ex);
            }
        }
    }
}
