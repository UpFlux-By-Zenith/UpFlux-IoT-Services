using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Repositories;
using UpFlux.Gateway.Server.Protos;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Periodically pings each device (based on the IP in DeviceRepository)
    /// to detect whether it's alive or offline. If status changes,
    /// we send a DeviceStatus to the Cloud via ControlChannelWorker.
    /// </summary>
    public class DevicePingService : BackgroundService
    {
        private readonly ILogger<DevicePingService> _logger;
        private readonly DeviceRepository _deviceRepository;
        private readonly ControlChannelWorker _controlChannelWorker;

        private readonly TimeSpan _pingInterval = TimeSpan.FromSeconds(2);

        // To store the last-known status in a local map:
        //  Key = deviceUuid
        //  Value = isOnline (true) or offline (false)
        private Dictionary<string, bool> _lastKnownStatus;

        public DevicePingService(
            ILogger<DevicePingService> logger,
            DeviceRepository deviceRepository,
            ControlChannelWorker controlChannelWorker)
        {
            _logger = logger;
            _deviceRepository = deviceRepository;
            _controlChannelWorker = controlChannelWorker;
            _lastKnownStatus = new Dictionary<string, bool>();
        }

        /// <summary>
        /// The main loop of this BackgroundService. 
        /// We run until the application stops, pinging devices periodically.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DevicePingService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PingAllDevicesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in PingAllDevicesAsync");
                }

                await Task.Delay(_pingInterval, stoppingToken);
            }

            _logger.LogInformation("DevicePingService stopping.");
        }

        /// <summary>
        /// Pings every device from the DB, compares with last-known status, 
        /// and if changed, then notifies the Cloud.
        /// </summary>
        private async Task PingAllDevicesAsync()
        {
            List<Device> devices = _deviceRepository.GetAllDevices();

            foreach (Device dev in devices)
            {
                if (string.IsNullOrWhiteSpace(dev.IPAddress))
                {
                    continue;
                }

                // Attempt ping
                bool isOnline = await PingHostAsync(dev.IPAddress);

                // Check if we have old state
                bool oldState = false;
                bool hadState = _lastKnownStatus.TryGetValue(dev.UUID, out oldState);

                if (!hadState || oldState != isOnline)
                {
                    // State changed or first time
                    _logger.LogInformation("Device {0} status changed to {1}",
                        dev.UUID, isOnline ? "ONLINE" : "OFFLINE");

                    _lastKnownStatus[dev.UUID] = isOnline;

                    dev.LastSeen = DateTime.UtcNow;
                    _deviceRepository.AddOrUpdateDevice(dev);

                    await SendDeviceStatusChange(dev.UUID, isOnline);
                }
            }
        }

        /// <summary>
        /// Actually does the ICMP ping with 1s timeout. 
        /// Returns true if the reply is Success, otherwise false.
        /// </summary>
        private async Task<bool> PingHostAsync(string ipOrHostname)
        {
            using (Ping ping = new Ping())
            {
                try
                {
                    PingReply reply = await ping.SendPingAsync(ipOrHostname, 1000);
                    return (reply.Status == IPStatus.Success);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Sends a DeviceStatus via the ControlChannelWorker.
        /// </summary>
        private async Task SendDeviceStatusChange(string deviceUuid, bool isOnline)
        {
            if (_controlChannelWorker == null)
            {
                _logger.LogWarning("No ControlChannelWorker, cannot send device status change.");
                return;
            }

            DeviceStatus statusMsg = new DeviceStatus
            {
                DeviceUuid = deviceUuid,
                IsOnline = isOnline,
                LastSeen = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };

            await _controlChannelWorker.SendDeviceStatusAsync(statusMsg);
        }
    }
}
