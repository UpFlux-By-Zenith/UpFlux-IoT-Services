using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Library.Models
{
    /// <summary>
    /// Represents memory metrics such as total, free, and used memory.
    /// </summary>
    public class MemoryMetrics
    {
        /// <summary>
        /// Total system memory in bytes
        /// </summary>
        public long TotalMemory { get; set; }
        /// <summary>
        /// Free system memory in bytes
        /// </summary>
        public long FreeMemory { get; set; }
        /// <summary>
        /// Used memory in bytes
        /// </summary>
        public long UsedMemory => TotalMemory - FreeMemory;
    }
}
