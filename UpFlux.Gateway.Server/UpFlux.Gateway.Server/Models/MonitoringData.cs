using System;
using System.Collections.Generic;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents the monitoring data received from a device.
    /// </summary>
    public class MonitoringData
    {
        /// <summary>
        /// Gets or sets the device UUID.
        /// </summary>
        public string UUID { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the data (UTC).
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the metrics data.
        /// </summary>
        public Metrics Metrics { get; set; }

        /// <summary>
        /// Gets or sets the sensor data.
        /// </summary>
        public SensorData SensorData { get; set; }
    }

    /// <summary>
    /// Represents the system metrics data.
    /// </summary>
    public class Metrics
    {
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double DiskUsage { get; set; }
        public NetworkUsage NetworkUsage { get; set; }
        public double CpuTemperature { get; set; }
        public double SystemUptime { get; set; }
    }

    /// <summary>
    /// Represents network usage data.
    /// </summary>
    public class NetworkUsage
    {
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
    }

    /// <summary>
    /// Represents sensor data.
    /// </summary>
    public class SensorData
    {
        public int RedValue { get; set; }
        public int GreenValue { get; set; }
        public int BlueValue { get; set; }
    }
}
