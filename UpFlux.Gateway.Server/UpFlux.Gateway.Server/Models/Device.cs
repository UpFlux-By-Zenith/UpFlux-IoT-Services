using System;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents a device connected to the Gateway Server.
    /// </summary>
    public class Device
    {
        /// <summary>
        /// Gets or sets the unique identifier (UUID) of the device.
        /// </summary>
        public string UUID { get; set; }

        /// <summary>
        /// Gets or sets the IP address of the device.
        /// </summary>
        public string IPAddress { get; set; }

        /// <summary>
        /// Gets or sets the license assigned to the device.
        /// </summary>
        public string License { get; set; }

        /// <summary>
        /// Gets or sets the license expiration date and time (UTC).
        /// </summary>
        public DateTime LicenseExpiration { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) when the device was last seen.
        /// </summary>
        public DateTime LastSeen { get; set; }

        /// <summary>
        /// Gets or sets the registration status of the device (e.g., Pending, Registered).
        /// </summary>
        public string RegistrationStatus { get; set; }

        public DateTime NextEarliestRenewalAttempt { get; set; }
    }
}
