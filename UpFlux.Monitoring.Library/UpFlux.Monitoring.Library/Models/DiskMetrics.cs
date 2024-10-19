using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Library.Models
{
    /// <summary>
    /// Represents disk usage metrics such as total space, free space, and used space.
    /// </summary>
    public class DiskMetrics
    {
        /// <summary>
        /// Total disk space in bytes
        /// </summary>
        public long TotalDiskSpace { get; set; }
        /// <summary>
        /// Free disk space in bytes
        /// </summary>
        public long FreeDiskSpace { get; set; }
        /// <summary>
        /// Used disk space in bytes
        /// </summary>
        public long UsedDiskSpace => TotalDiskSpace - FreeDiskSpace;
    }
}
