using System;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents an update package received from the cloud.
    /// </summary>
    public class UpdatePackage
    {
        /// <summary>
        /// Gets or sets the unique identifier of the update package.
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// Gets or sets the version of the update package.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the filename of the update package.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the path where the update package is stored.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Gets or sets the digital signature of the update package.
        /// </summary>
        public byte[] Signature { get; set; }

        /// <summary>
        /// Gets or sets the list of target device UUIDs for this update.
        /// </summary>
        public string[] TargetDevices { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) when the update package was received.
        /// </summary>
        public DateTime ReceivedAt { get; set; }
    }
}
