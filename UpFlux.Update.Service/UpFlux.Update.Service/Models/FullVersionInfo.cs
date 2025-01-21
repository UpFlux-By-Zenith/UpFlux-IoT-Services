using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Update.Service.Models
{
    public class FullVersionInfo
    {
        public VersionRecord Current { get; set; }
        public List<VersionRecord> Available { get; set; }
    }
}
