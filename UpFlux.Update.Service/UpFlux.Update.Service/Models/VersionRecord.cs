﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Update.Service.Models
{
    public class VersionRecord
    {
        public string Version { get; set; }
        public DateTime InstalledAt { get; set; }
    }
}
