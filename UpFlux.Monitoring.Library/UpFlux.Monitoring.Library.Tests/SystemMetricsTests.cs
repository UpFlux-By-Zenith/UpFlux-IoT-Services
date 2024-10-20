using NUnit.Framework;
using UpFlux.Monitoring.Library.Services;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Library.Tests
{
    [TestFixture]
    public class SystemMetricsTests
    {
        private CpuMetricsService _cpuMetricsService;
        private MemoryMetricsService _memoryMetricsService;
        private NetworkMetricsService _networkMetricsService;
        private DiskMetricsService _diskMetricsService;
        private SystemUptimeService _systemUptimeService;
        private CpuTemperatureService _cpuTemperatureService;

        [SetUp]
        public void Setup()
        {
            _cpuMetricsService = new CpuMetricsService();
            _memoryMetricsService = new MemoryMetricsService();
            _networkMetricsService = new NetworkMetricsService();
            _diskMetricsService = new DiskMetricsService();
            _systemUptimeService = new SystemUptimeService();
            _cpuTemperatureService = new CpuTemperatureService();
        }

        [Test]
        public void CpuMetricsService_ShouldReturnValidMetrics()
        {
            CpuMetrics metrics = _cpuMetricsService.GetCpuMetrics();
            Assert.That(metrics, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(metrics.CurrentUsage, Is.GreaterThanOrEqualTo(0));
                Assert.That(metrics.LoadAverage, Is.GreaterThanOrEqualTo(0));
            });
        }

        [Test]
        public void MemoryMetricsService_ShouldReturnValidMetrics()
        {
            MemoryMetrics metrics = _memoryMetricsService.GetMemoryMetrics();
            Assert.That(metrics, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(metrics.TotalMemory, Is.GreaterThan(0));
                Assert.That(metrics.FreeMemory, Is.GreaterThanOrEqualTo(0));
                Assert.That(metrics.UsedMemory, Is.GreaterThan(0));
            });
        }

        [Test]
        public void NetworkMetricsService_ShouldReturnValidMetrics()
        {
            NetworkMetrics metrics = _networkMetricsService.GetNetworkMetrics();
            Assert.That(metrics, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(metrics.ReceivedBytes, Is.GreaterThanOrEqualTo(0));
                Assert.That(metrics.TransmittedBytes, Is.GreaterThanOrEqualTo(0));
            });
        }

        [Test]
        public void DiskMetricsService_ShouldReturnValidMetrics()
        {
            DiskMetrics metrics = _diskMetricsService.GetDiskMetrics();
            Assert.That(metrics, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(metrics.TotalDiskSpace, Is.GreaterThan(0));
                Assert.That(metrics.FreeDiskSpace, Is.GreaterThan(0));
                Assert.That(metrics.UsedDiskSpace, Is.GreaterThan(0));
            });
        }

        [Test]
        public void SystemUptimeService_ShouldReturnValidMetrics()
        {
            SystemUptimeMetrics metrics = _systemUptimeService.GetUptime();
            Assert.That(metrics, Is.Not.Null);
            Assert.That(metrics.UptimeSeconds, Is.GreaterThan(0));
        }

        [Test]
        public void CpuTemperatureService_ShouldReturnValidMetrics()
        {
            CpuTemperatureMetrics metrics = _cpuTemperatureService.GetCpuTemperature();
            Assert.That(metrics, Is.Not.Null);
            Assert.That(metrics.TemperatureCelsius, Is.GreaterThan(0));
        }
    }
}
