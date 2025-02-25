using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Service
{
    /// <summary>
    /// Represents whether the device is actively sending data (Busy) 
    /// or remains silent (Idle).
    /// </summary>
    public enum SimulationState
    {
        /// <summary>
        /// Device is actively sending data every 5s (like a machine in use).
        /// </summary>
        Busy,

        /// <summary>
        /// Device is idle and not sending any data to the Gateway.
        /// </summary>
        Idle
    }

}
