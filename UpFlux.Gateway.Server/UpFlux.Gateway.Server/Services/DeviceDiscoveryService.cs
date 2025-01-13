using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Services;
using UpFlux.Gateway.Server.Repositories;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for discovering devices on the local network.
    /// </summary>
    public class DeviceDiscoveryService : BackgroundService
    {
        private readonly ILogger<DeviceDiscoveryService> _logger;
        private readonly GatewaySettings _settings;
        private readonly DeviceCommunicationService _deviceCommunicationService;
        private readonly DeviceRepository _deviceRepository;

        // ConcurrentDictionary to store known devices for thread-safe operations
        private readonly ConcurrentDictionary<string, Device> _knownDevices;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceDiscoveryService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="settings">Gateway settings.</param>
        /// <param name="deviceCommunicationService">Service for device communication.</param>
        /// <param name="deviceRepository">Repository for device data.</param>
        public DeviceDiscoveryService(
            ILogger<DeviceDiscoveryService> logger,
            IOptions<GatewaySettings> settings,
            DeviceCommunicationService deviceCommunicationService,
            DeviceRepository deviceRepository)
        {
            _logger = logger;
            _settings = settings.Value;
            _deviceCommunicationService = deviceCommunicationService;
            _deviceRepository = deviceRepository;

            // Initialize known devices from the repository
            List<Device> devices = _deviceRepository.GetAllDevices();
            _knownDevices = new ConcurrentDictionary<string, Device>(devices.ToDictionary(d => d.IPAddress));
        }

        /// <summary>
        /// Executes the device discovery service.
        /// </summary>
        /// <param name="stoppingToken">Token to signal cancellation.</param>
        /// <returns>A task representing the execution.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DeviceDiscoveryService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ScanNetworkAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred during network scanning.");
                }

                await Task.Delay(TimeSpan.FromSeconds(_settings.DeviceScanIntervalSeconds), stoppingToken);
            }

            _logger.LogInformation("DeviceDiscoveryService is stopping.");
        }

        /// <summary>
        /// Scans the local network for devices.
        /// </summary>
        /// <returns>A task representing the scanning operation.</returns>
        private async Task ScanNetworkAsync()
        {
            _logger.LogInformation("Scanning network for devices...");

            // Get the local IP addresses
            List<IPAddress> localIPs = GetLocalIPAddresses();

            // Assuming a subnet mask of /24 for simplicity
            List<IPAddress> ipAddressesToScan = new List<IPAddress>();

            foreach (IPAddress localIP in localIPs)
            {
                byte[] baseIP = localIP.GetAddressBytes();
                for (int i = 1; i < 255; i++)
                {
                    baseIP[3] = (byte)i;
                    IPAddress ip = new IPAddress(baseIP);
                    ipAddressesToScan.Add(ip);
                }
            }

            List<Task<Tuple<IPAddress, bool>>> pingTasks = ipAddressesToScan.Select(ip => PingAsync(ip)).ToList();
            Tuple<IPAddress, bool>[] pingResults = await Task.WhenAll(pingTasks);

            List<string> activeIPs = pingResults.Where(pr => pr.Item2).Select(pr => pr.Item1.ToString()).ToList();

            // Detect new devices
            foreach (string ip in activeIPs)
            {
                if (!_knownDevices.ContainsKey(ip))
                {
                    _logger.LogInformation("New device detected at IP: {ip}", ip);

                    // Attempt to establish a secure connection
                    await _deviceCommunicationService.InitiateSecureConnectionAsync(ip);

                    // Add to known devices (Placeholder device object)
                    Device device = new Device { IPAddress = ip };
                    _knownDevices.TryAdd(ip, device);

                    // Save to the repository
                    _deviceRepository.AddOrUpdateDevice(device);
                }
            }
        }

        /// <summary>
        /// Pings an IP address to check if it's active.
        /// </summary>
        /// <param name="ip">The IP address to ping.</param>
        /// <returns>A tuple containing the IP address and a boolean indicating if it's active.</returns>
        private async Task<Tuple<IPAddress, bool>> PingAsync(IPAddress ip)
        {
            using Ping ping = new Ping();
            try
            {
                PingReply reply = await ping.SendPingAsync(ip, 1000);
                return new Tuple<IPAddress, bool>(ip, reply.Status == IPStatus.Success);
            }
            catch
            {
                return new Tuple<IPAddress, bool>(ip, false);
            }
        }

        /// <summary>
        /// Retrieves local IP addresses of the machine.
        /// </summary>
        /// <returns>A list of local IP addresses.</returns>
        private List<IPAddress> GetLocalIPAddresses()
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            //return host.AddressList.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).ToList();
            return NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.Name == _settings.DeviceNetworkInterface)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(ua => ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        .Select(ua => ua.Address)
        .ToList();
        }
    }
}
