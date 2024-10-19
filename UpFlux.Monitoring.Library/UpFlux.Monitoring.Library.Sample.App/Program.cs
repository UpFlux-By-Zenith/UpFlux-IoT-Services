using UpFlux.Monitoring.Library.Services;
using UpFlux.Monitoring.Library.Models;

namespace UpFlux.Monitoring.Library.Sample.App
{
    class Program
    {
        static void Main(string[] args)
        {
            CpuMetricsService cpuService = new CpuMetricsService();
            MemoryMetricsService memoryService = new MemoryMetricsService();
            NetworkMetricsService networkService = new NetworkMetricsService();
            DiskMetricsService diskService = new DiskMetricsService();
            SystemUptimeService uptimeService = new SystemUptimeService();
            CpuTemperatureService temperatureService = new CpuTemperatureService();
            BandwidthMetricsService bandwidthService = new BandwidthMetricsService();

            while (true)
            {
                Console.Clear();
                Console.WriteLine("Raspberry Pi Monitoring Menu");
                Console.WriteLine("1. Get CPU Metrics");
                Console.WriteLine("2. Get Memory Metrics");
                Console.WriteLine("3. Get Network Metrics");
                Console.WriteLine("4. Get Disk Metrics");
                Console.WriteLine("5. Get System Uptime");
                Console.WriteLine("6. Get CPU Temperature");
                Console.WriteLine("7. Get Bandwidth Metrics");
                Console.WriteLine("8. Exit");
                Console.WriteLine("Select an option:");

                string? choice = Console.ReadLine();
                switch (choice)
                {
                    case "1":
                        CpuMetrics cpuMetrics = cpuService.GetCpuMetrics();
                        Console.WriteLine($"CPU Usage: {cpuMetrics.CurrentUsage}%");
                        Console.WriteLine($"Load Average: {cpuMetrics.LoadAverage}");
                        break;
                    case "2":
                        MemoryMetrics memoryMetrics = memoryService.GetMemoryMetrics();
                        Console.WriteLine($"Total Memory: {memoryMetrics.TotalMemory} bytes");
                        Console.WriteLine($"Free Memory: {memoryMetrics.FreeMemory} bytes");
                        Console.WriteLine($"Used Memory: {memoryMetrics.UsedMemory} bytes");
                        break;
                    case "3":
                        NetworkMetrics networkMetrics = networkService.GetNetworkMetrics();
                        Console.WriteLine($"Received Bytes: {networkMetrics.ReceivedBytes}");
                        Console.WriteLine($"Transmitted Bytes: {networkMetrics.TransmittedBytes}");
                        break;
                    case "4":
                        DiskMetrics diskMetrics = diskService.GetDiskMetrics();
                        Console.WriteLine($"Total Disk Space: {diskMetrics.TotalDiskSpace} bytes");
                        Console.WriteLine($"Free Disk Space: {diskMetrics.FreeDiskSpace} bytes");
                        Console.WriteLine($"Used Disk Space: {diskMetrics.UsedDiskSpace} bytes");
                        break;
                    case "5":
                        SystemUptimeMetrics uptimeMetrics = uptimeService.GetUptime();
                        Console.WriteLine($"System Uptime: {uptimeMetrics.UptimeSeconds} seconds");
                        break;
                    case "6":
                        CpuTemperatureMetrics cpuTemperatureMetrics = temperatureService.GetCpuTemperature();
                        Console.WriteLine($"CPU Temperature: {cpuTemperatureMetrics.TemperatureCelsius} °C");
                        break;
                    case "7":
                        BandwidthMetrics bandwidthMetrics = bandwidthService.GetBandwidthMetrics();
                        Console.WriteLine($"Download Speed: {bandwidthMetrics.DownloadSpeed} Kbps");
                        Console.WriteLine($"Upload Speed: {bandwidthMetrics.UploadSpeed} Kbps");
                        break;
                    case "8":
                        return;
                    default:
                        Console.WriteLine("Invalid option, please try again.");
                        break;
                }

                Console.WriteLine("\nPress any key to return to the menu...");
                Console.ReadKey();
            }
        }
    }
}
