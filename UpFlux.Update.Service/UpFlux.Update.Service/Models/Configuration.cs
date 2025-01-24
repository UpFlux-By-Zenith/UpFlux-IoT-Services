using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace UpFlux.Update.Service.Models
{
    /// <summary>
    /// Holds configuration settings for the Update Service.
    /// </summary>
    public class Configuration
    {
        [Required]
        public string GatewayServerIp { get; set; }

        [Required]
        public int GatewayServerPort { get; set; }

        [Required]
        public int DeviceServerPort { get; set; }

        [Required]
        public string IncomingPackageDirectory { get; set; }

        [Required]
        public string PackageDirectory { get; set; }

        public int MaxStoredVersions { get; set; }

        [Required]
        public string MonitoringServiceLog { get; set; }

        [Required]
        public string UpfluxLogPath { get; set; }

        public int SimulationTimeoutSeconds { get; set; }

        public int PostInstallationMonitoringMinutes { get; set; }

        public List<string> ErrorPatterns { get; set; }

        [Required]
        public string PackageNamePattern { get; set; }

        [Required]
        public string DeviceUuid { get; set; }

        [Required]
        public string LicenseFilePath { get; set; }
    }
}
