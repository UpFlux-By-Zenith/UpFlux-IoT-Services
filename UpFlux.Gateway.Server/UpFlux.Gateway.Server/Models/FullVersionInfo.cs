using System;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents a list of the software version installed on a device.
    /// </summary>
    public class FullVersionInfo
    {
        public VersionRecord Current { get; set; }
        public List<VersionRecord> Available { get; set; }
    }
}
