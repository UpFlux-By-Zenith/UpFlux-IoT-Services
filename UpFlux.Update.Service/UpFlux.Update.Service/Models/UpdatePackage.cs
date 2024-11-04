using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Update.Service.Models
{
    /// <summary>
    /// Represents an update package for the UpFlux Monitoring Service.
    /// </summary>
    public class UpdatePackage
    {
        public string FilePath { get; set; }
        public string Version { get; set; }
    }
}

