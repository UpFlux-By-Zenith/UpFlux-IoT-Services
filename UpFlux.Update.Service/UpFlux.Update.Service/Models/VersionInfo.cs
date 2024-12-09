using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Update.Service.Models
{
    public class VersionInfo
    {
        public string CurrentVersion { get; set; }
        public List<string> AvailableVersions { get; set; }
    }
}
