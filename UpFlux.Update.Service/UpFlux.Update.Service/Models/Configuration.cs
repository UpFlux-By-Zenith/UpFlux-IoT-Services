using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Update.Service.Models
{
    /// <summary>
    /// Holds configuration settings for the Update Service.
    /// </summary>
    public class Configuration
    {
        public string GatewayServerIp { get; set; }
        public int GatewayServerPort { get; set; }
        public string GatewayServerLogEndpoint { get; set; }
        public string IncomingPackageDirectory { get; set; }
        public string PackageDirectory { get; set; }
        public int MaxStoredVersions { get; set; }
        public string MonitoringServiceLog { get; set; }
        public string UpdateServiceLog { get; set; }
        public int SimulationTimeoutSeconds { get; set; }
        public int PostInstallationMonitoringMinutes { get; set; }
        public List<string> ErrorPatterns { get; set; }
        public string PackageNamePattern { get; set; }
    }
}
