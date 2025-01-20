using UpFlux.Gateway.Server.Protos;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents aggregated data for cloud upload.
    /// </summary>
    public class AggregatedData
    {
        public string UUID { get; set; }
        public DateTime Timestamp { get; set; }

        // Flattened aggregator metrics
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double DiskUsage { get; set; }
        public NetworkUsage NetworkUsage { get; set; }
        public double CpuTemperature { get; set; }
        public double SystemUptime { get; set; }

        // Aggregated sensor data
        public SensorData SensorData { get; set; }
    }

    public class NetworkUsage
    {
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
    }
}
