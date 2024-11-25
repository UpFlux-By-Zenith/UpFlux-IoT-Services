using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents the configuration settings for the UpFlux Gateway Server.
    /// </summary>
    public class GatewaySettings
    {
        /// <summary>
        /// Gets or sets the address of the cloud server to communicate with.
        /// </summary>
        [Required]
        public string CloudServerAddress { get; set; }

        /// <summary>
        /// Gets or sets the path to the Gateway Server's certificate file.
        /// </summary>
        [Required]
        public string CertificatePath { get; set; }

        /// <summary>
        /// Gets or sets the password for the Gateway Server's certificate file.
        /// </summary>
        public string CertificatePassword { get; set; }

        /// <summary>
        /// Gets or sets the path to the trusted CA certificate file.
        /// </summary>
        [Required]
        public string TrustedCaCertificatePath { get; set; }

        /// <summary>
        /// Gets or sets the connection string for the SQLite database.
        /// </summary>
        [Required]
        public string DatabaseConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the interval in seconds for scanning the network for devices.
        /// </summary>
        public int DeviceScanIntervalSeconds { get; set; } = 60;

        /// <summary>
        /// Gets or sets the interval in minutes for checking license expirations.
        /// </summary>
        public int LicenseCheckIntervalMinutes { get; set; } = 60;

        /// <summary>
        /// Gets or sets the TCP port on which the Gateway Server listens for device connections.
        /// </summary>
        public int TcpPort { get; set; } = 5000;

        /// <summary>
        /// Gets or sets the interval in seconds for data aggregation.
        /// </summary>
        public int DataAggregationIntervalSeconds { get; set; } = 300; // Default to 5 minutes
    }
}

