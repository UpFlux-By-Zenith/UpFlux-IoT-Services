using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Service
{
    /// <summary>
    /// Represents the sensor data received from the Python script.
    /// </summary>
    public class SensorData
    {
        public int red_value { get; set; }
        public int green_value { get; set; }
        public int blue_value { get; set; }
    }
}
