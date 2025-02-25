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
        /// Gets or sets the unique identifier of the gateway.
        /// </summary>
        public string GatewayId { get; set; }

        /// <summary>
        /// Gets or sets the address of the cloud server to communicate with.
        /// </summary>
        [Required]
        public string CloudServerAddress { get; set; }

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
        /// Gets or sets the IP address of the Gateway Server.
        /// </summary>
        public string GatewayServerIp { get; set; }

        /// <summary>
        /// Gets or sets the TCP port on which the Gateway Server listens for device connections.
        /// </summary>
        public int GatewayTcpPort { get; set; } = 5000;

        /// <summary>
        /// Gets or sets the TCP port on which the device listens for Gateway connections.
        /// </summary>
        public int DeviceTcpPort { get; set; } = 6000;

        /// <summary>
        /// Gets or sets the interval in seconds for data aggregation.
        /// </summary>
        public int DataAggregationIntervalSeconds { get; set; } = 300; // Default to 5 minutes

        /// <summary>
        /// Gets or sets the path to the public key used to verify update packages.
        /// </summary>
        public string UpdatePackagePublicKeyPath { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of retries for sending updates to devices.
        /// </summary>
        public int UpdateMaxRetries { get; set; } = 3;

        /// <summary>
        /// Gets or sets the directory path for storing device logs.
        /// </summary>
        [Required]
        public string LogsDirectory { get; set; }

        /// <summary>
        /// Gets or sets the directory path for storing update packages.
        /// </summary>
        public string UpdatePackageDirectory { get; set; }

        /// <summary>
        /// The Network Interface to use by gateway to speak with devices
        /// </summary>
        public string DeviceNetworkInterface { get; set; }

        /// <summary>
        /// Holds the address of the AI service
        /// </summary>
        public string AiServiceAddress { get; set; }
    }
}

