using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace UpFlux.Monitoring.Service
{
    /// <summary>
    /// Represents the settings for the service
    /// </summary>
    public class ServiceSettings
    {
        /// <summary>
        /// The ip address of the gateway server
        /// </summary>
        [Required]
        public string ServerIp { get; set; }

        /// <summary>
        /// The port to connect to the gateway server
        /// </summary>
        [Required]
        public int ServerPort { get; set; }

        /// <summary>
        /// The path to the sensor script
        /// </summary>
        [Required]
        public string SensorScriptPath { get; set; }

        /// <summary>
        /// The interval in seconds to monitor the network interface
        /// </summary>
        public int MonitoringIntervalSeconds { get; set; } = 10;

        /// <summary>
        /// The primary network interface to monitor
        /// </summary>
        public string NetworkInterface { get; set; } = "eth0";

        /// <summary>
        /// The UUID of the device
        /// </summary>
        [Required]
        public string DeviceUuid { get; set; }

        /// <summary>
        /// The path to store the license file
        /// </summary>
        [Required]
        public string LicenseFilePath { get; set; }
    }
}

