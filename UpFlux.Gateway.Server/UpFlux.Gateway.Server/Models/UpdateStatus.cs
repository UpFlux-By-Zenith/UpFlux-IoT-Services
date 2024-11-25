using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents the status of an update package distribution.
    /// </summary>
    public class UpdateStatus
    {
        /// <summary>
        /// Gets or sets the package identifier.
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// Gets or sets the version of the update package.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the list of target device UUIDs.
        /// </summary>
        public string[] TargetDevices { get; set; }

        /// <summary>
        /// Gets or sets the set of devices that have successfully received the update.
        /// </summary>
        public HashSet<string> DevicesSucceeded { get; set; }

        /// <summary>
        /// Gets or sets the set of devices that failed to receive the update.
        /// </summary>
        public HashSet<string> DevicesFailed { get; set; }

        /// <summary>
        /// Gets or sets the set of devices that are pending update delivery.
        /// </summary>
        public HashSet<string> DevicesPending { get; set; }
    }
}
