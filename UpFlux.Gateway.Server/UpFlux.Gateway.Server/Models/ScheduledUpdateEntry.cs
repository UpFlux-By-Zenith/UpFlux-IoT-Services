using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// This class represents a scheduled update entry that is used to schedule updatres for devices.
    /// </summary>
    public class ScheduledUpdateEntry
    {
        public string ScheduleId { get; set; }
        public List<string> DeviceUuids { get; set; }
        public string FileName { get; set; }
        public byte[] PackageData { get; set; }
        public DateTime StartTimeUtc { get; set; }
    }
}

