using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Holds a sliding 6-minute window of usage data for each device:
    ///  - CPU usage samples
    ///  - Memory usage samples
    ///  - Network usage samples
    ///  - Timestamps of messages (to calculate the Busy fraction)
    /// 
    /// 
    /// AI is called every 1-5 minutes, to compute:
    ///   1) BusyFraction (based on # of messages in the last 6 minutes)
    ///   2) AvgCpu, AvgMem, AvgNet
    /// </summary>
    public class DeviceUsageAggregator
    {
        private readonly ILogger<DeviceUsageAggregator> _logger;

        // For each device, store a list of usage samples 
        // that occurred in the last 6 minutes
        private ConcurrentDictionary<string, List<UsageSample>> _deviceSamples;

        // keep data for up to 6 minutes (360 seconds).
        private readonly TimeSpan _window = TimeSpan.FromMinutes(6);

        // if the device tries to send data every 3s if Busy.
        // That means in 6 minutes, a max of 120 samples are expected.
        // remove older samples that exceed that window.
        public DeviceUsageAggregator(ILogger<DeviceUsageAggregator> logger)
        {
            _logger = logger;
            _deviceSamples = new ConcurrentDictionary<string, List<UsageSample>>();
        }

        /// <summary>
        /// Called by DeviceCommunicationService whenever monitoring data is received
        /// from a device. We parse out CPU, mem, net usage and store it.
        /// </summary>
        public void RecordUsage(
            string deviceUuid,
            double cpuUsagePercent,
            double memoryUsagePercent,
            double networkBytesSent,
            double networkBytesReceived
        )
        {
            DateTime now = DateTime.UtcNow;

            // Get or create the list of usage samples for this device
            List<UsageSample> samples = _deviceSamples.GetOrAdd(deviceUuid, _ => new List<UsageSample>());
            lock (samples)
            {
                // Add new sample
                samples.Add(new UsageSample
                {
                    Timestamp = now,
                    Cpu = cpuUsagePercent,
                    Mem = memoryUsagePercent,
                    NetSent = networkBytesSent,
                    NetRecv = networkBytesReceived
                });

                // Remove samples older than 6 minutes from now
                DateTime cutoff = now - _window;
                samples.RemoveAll(s => s.Timestamp < cutoff);
            }
        }

        /// <summary>
        /// Computes feature vectors for each device based on the last 6 minutes of data:
        ///   - Busy fraction (# of samples in last 6 min / expected # if device is truly busy)
        ///   - Average CPU usage
        ///   - Average memory usage
        ///   - Average network usage (sent+recv) 
        /// 
        /// This data will be sent to the AI microservice for clustering + scheduling.
        /// </summary>
        public List<DeviceUsageVector> ComputeUsageVectors()
        {
            List<DeviceUsageVector> result = new List<DeviceUsageVector>();
            DateTime now = DateTime.UtcNow;

            foreach (KeyValuePair<string, List<UsageSample>> kvp in _deviceSamples)
            {
                string deviceUuid = kvp.Key;
                List<UsageSample> samples = kvp.Value;

                // To consider only samples in [now - 6min, now].
                //   count = # of samples
                //   BusyFraction = count / maxPossible
                //   avg cpu
                //   avg mem
                //   avg net
                double cpuSum = 0;
                double memSum = 0;
                double netSum = 0;
                int count = 0;

                lock (samples)
                {
                    foreach (var s in samples)
                    {
                        count++;
                        cpuSum += s.Cpu;
                        memSum += s.Mem;
                        netSum += (s.NetSent + s.NetRecv);
                    }
                }

                if (count == 0)
                {
                    // device might be silent treat as inactive
                    continue;
                }

                // if a device is "truly busy" if it sends data every 3s:
                //  in 6 minutes, that's 120 samples. 
                //  So busyFraction = count / 120.
                int maxPossible = (int)(_window.TotalSeconds / 3.0);
                double busyFraction = (double)count / maxPossible;
                double cpuAvg = cpuSum / count;
                double memAvg = memSum / count;
                double netAvg = netSum / count;

                var vec = new DeviceUsageVector
                {
                    DeviceUuid = deviceUuid,
                    BusyFraction = busyFraction,
                    AvgCpu = cpuAvg,
                    AvgMem = memAvg,
                    AvgNet = netAvg
                };
                result.Add(vec);
            }

            return result;
        }
    }



    /// <summary>
    /// Represents one usage sample from a single MonitoringData message.
    /// </summary>
    public class UsageSample
    {
        public DateTime Timestamp { get; set; }
        public double Cpu { get; set; }
        public double Mem { get; set; }
        public double NetSent { get; set; }
        public double NetRecv { get; set; }
    }

    /// <summary>
    /// Final feature vector we send to the AI microservice:
    ///   BusyFraction, plus average CPU, memory, network usage
    ///   (covering last 6 minutes)
    /// </summary>
    public class DeviceUsageVector
    {
        public string DeviceUuid { get; set; }
        public double BusyFraction { get; set; }
        public double AvgCpu { get; set; }
        public double AvgMem { get; set; }
        public double AvgNet { get; set; }
    }

    /// <summary>
    /// The aggregator's guessed next idle window.
    /// If NextIdleTime is null => aggregator can't find a 20s window.
    /// </summary>
    public class DeviceIdleInfo
    {
        public string DeviceUuid { get; set; }
        public DateTime? NextIdleTime { get; set; }
        public int IdleDurationSecs { get; set; }
    }
}
