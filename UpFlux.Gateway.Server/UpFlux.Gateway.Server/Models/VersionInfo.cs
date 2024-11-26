using System;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents a software version installed on a device.
    /// </summary>
    public class VersionInfo
    {
        /// <summary>
        /// Gets or sets the device UUID.
        /// </summary>
        public string DeviceUUID { get; set; }

        /// <summary>
        /// Gets or sets the software version installed on the device.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) when this version was installed or detected.
        /// </summary>
        public DateTime InstalledAt { get; set; }
    }
}
