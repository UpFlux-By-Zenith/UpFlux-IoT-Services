using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// A minimal interface for sending a license to a device.
    /// </summary>
    public interface ILicensePusher
    {
        /// <summary>
        /// Sends a license string to the specified device.
        /// </summary>
        /// <param name="uuid">The UUID of the device</param>
        /// <param name="license">The license string</param>
        /// <returns>Returns true if success; false otherwise</returns>
        Task<bool> SendLicenseAsync(string uuid, string license);
    }
}
