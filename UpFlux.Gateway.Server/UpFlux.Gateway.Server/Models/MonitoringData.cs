using System;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents the monitoring data received from a device.
    /// Matches the nested JSON structure from the device.
    /// </summary>
    public class MonitoringData
    {
        /// <summary>
        /// Gets or sets the device UUID.
        /// </summary>
        public string UUID { get; set; }

        /// <summary>
        /// Nested metrics sub-objects (Cpu, Memory, etc.).
        /// </summary>
        public DeviceMetrics Metrics { get; set; }

        /// <summary>
        /// Sensor data for red/green/blue, etc.
        /// </summary>
        public SensorData SensorData { get; set; }
    }

    /// <summary>
    /// Top-level container for all sub-metrics from the device,
    /// like "CpuMetrics", "MemoryMetrics", "NetworkMetrics"
    /// </summary>
    public class DeviceMetrics
    {
        public CpuMetrics CpuMetrics { get; set; }
        public MemoryMetrics MemoryMetrics { get; set; }
        public NetworkMetrics NetworkMetrics { get; set; }
        public DiskMetrics DiskMetrics { get; set; }
        public SystemUptimeMetrics SystemUptimeMetrics { get; set; }
        public CpuTemperatureMetrics CpuTemperatureMetrics { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// CPU usage stats as sent by the device (CurrentUsage, LoadAverage).
    /// </summary>
    public class CpuMetrics
    {
        public double CurrentUsage { get; set; }
        public double LoadAverage { get; set; }
    }

    /// <summary>
    /// Memory usage stats (Total, Free, Used).
    /// </summary>
    public class MemoryMetrics
    {
        public long TotalMemory { get; set; }
        public long FreeMemory { get; set; }
        public long UsedMemory { get; set; }
    }

    /// <summary>
    /// Network usage stats (RX/TX).
    /// </summary>
    public class NetworkMetrics
    {
        public long ReceivedBytes { get; set; }
        public long TransmittedBytes { get; set; }
    }

    /// <summary>
    /// Disk usage stats (Total, Free, Used).
    /// </summary>
    public class DiskMetrics
    {
        public long TotalDiskSpace { get; set; }
        public long FreeDiskSpace { get; set; }
        public long UsedDiskSpace { get; set; }
    }

    /// <summary>
    /// System uptime in seconds.
    /// </summary>
    public class SystemUptimeMetrics
    {
        public long UptimeSeconds { get; set; }
    }

    /// <summary>
    /// CPU temperature metrics (in Celsius).
    /// </summary>
    public class CpuTemperatureMetrics
    {
        public double TemperatureCelsius { get; set; }
    }

    /// <summary>
    /// Represents sensor data (R/G/B) from the device.
    /// </summary>
    public class SensorData
    {
        public int RedValue { get; set; }
        public int GreenValue { get; set; }
        public int BlueValue { get; set; }
    }
}
