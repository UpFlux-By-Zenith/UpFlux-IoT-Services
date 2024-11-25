using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Repositories;
using UpFlux.Gateway.Server.Protos;
using UpFlux.Gateway.Server.Services;
using System.Globalization;
using Google.Protobuf.WellKnownTypes;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for validating and managing device licenses.
    /// </summary>
    public class LicenseValidationService
    {
        private readonly ILogger<LicenseValidationService> _logger;
        private readonly GatewaySettings _settings;
        private readonly DeviceRepository _deviceRepository;
        private readonly CloudCommunicationService _cloudCommunicationService;
        private readonly DeviceCommunicationService _deviceCommunicationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="LicenseValidationService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="settings">Gateway settings.</param>
        /// <param name="deviceRepository">Repository for device data.</param>
        /// <param name="cloudCommunicationService">Service for cloud communication.</param>
        /// <param name="deviceCommunicationService">Service for device communication.</param>
        public LicenseValidationService(
            ILogger<LicenseValidationService> logger,
            IOptions<GatewaySettings> settings,
            DeviceRepository deviceRepository,
            CloudCommunicationService cloudCommunicationService,
            DeviceCommunicationService deviceCommunicationService)
        {
            _logger = logger;
            _settings = settings.Value;
            _deviceRepository = deviceRepository;
            _cloudCommunicationService = cloudCommunicationService;
            _deviceCommunicationService = deviceCommunicationService;
        }

        /// <summary>
        /// Validates the license for a device with the specified UUID.
        /// </summary>
        /// <param name="uuid">The device UUID.</param>
        /// <returns>A task representing the validation operation, with a boolean result indicating if the license is valid.</returns>
        public async Task<bool> ValidateLicenseAsync(string uuid)
        {
            _logger.LogInformation("Validating license for device UUID: {uuid}", uuid);

            Device device = _deviceRepository.GetDeviceByUuid(uuid);

            if (device == null)
            {
                _logger.LogInformation("Device UUID: {uuid} not found. Initiating registration.", uuid);
                await RegisterDeviceAsync(uuid);
            }
            else if (device.LicenseExpiration <= DateTime.UtcNow)
            {
                _logger.LogInformation("License for device UUID: {uuid} is expired or nearing expiration. Initiating renewal.", uuid);
                await RenewLicenseAsync(device);
            }
            else
            {
                _logger.LogInformation("Device UUID: {uuid} has a valid license.", uuid);
                return true;
            }

            // Refresh device info
            device = _deviceRepository.GetDeviceByUuid(uuid);

            // Return the license validity status
            return device.LicenseExpiration > DateTime.UtcNow;
        }

        /// <summary>
        /// Registers a new device by communicating with the cloud.
        /// </summary>
        /// <param name="uuid">The device UUID.</param>
        /// <returns>A task representing the registration operation.</returns>
        private async Task RegisterDeviceAsync(string uuid)
        {
            try
            {
                DeviceRegistrationResponse licenseResponse = await _cloudCommunicationService.RegisterDeviceAsync(uuid);

                if (licenseResponse.Approved)
                {
                    Device device = new Device
                    {
                        UUID = uuid,
                        License = licenseResponse.License,
                        LicenseExpiration = licenseResponse.ExpirationDate.ToDateTime()
                    };

                    _deviceRepository.AddOrUpdateDevice(device);

                    _logger.LogInformation("Device UUID: {uuid} registered successfully.", uuid);

                    // Send license to the device
                    await _deviceCommunicationService.SendLicenseAsync(uuid, licenseResponse.License);
                }
                else
                {
                    _logger.LogWarning("Device UUID: {uuid} registration was not approved by the cloud.", uuid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while registering device UUID: {uuid}", uuid);
            }
        }

        /// <summary>
        /// Renews the license for an existing device.
        /// </summary>
        /// <param name="device">The device whose license needs renewal.</param>
        /// <returns>A task representing the renewal operation.</returns>
        private async Task RenewLicenseAsync(Device device)
        {
            try
            {
                LicenseRenewalResponse renewalResponse = await _cloudCommunicationService.RenewLicenseAsync(device.UUID);

                if (renewalResponse.Approved)
                {
                    device.License = renewalResponse.License;
                    device.LicenseExpiration = renewalResponse.ExpirationDate.ToDateTime();

                    _deviceRepository.AddOrUpdateDevice(device);

                    _logger.LogInformation("License for device UUID: {uuid} renewed successfully.", device.UUID);

                    // Send updated license to the device
                    await _deviceCommunicationService.SendLicenseAsync(device.UUID, renewalResponse.License);
                }
                else
                {
                    _logger.LogWarning("License renewal for device UUID: {uuid} was not approved by the cloud.", device.UUID);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while renewing license for device UUID: {uuid}", device.UUID);
            }
        }

        /// <summary>
        /// Schedules periodic license checks and renewals.
        /// </summary>
        /// <param name="stoppingToken">Token to signal cancellation.</param>
        /// <returns>A task representing the scheduled operation.</returns>
        public async Task ScheduleLicenseRenewalsAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting scheduled license renewals.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    List<Device> devices = _deviceRepository.GetAllDevices();

                    foreach (Device device in devices)
                    {
                        // Check if the license is expiring within the next day
                        if (device.LicenseExpiration <= DateTime.UtcNow.AddDays(1))
                        {
                            _logger.LogInformation("License for device UUID: {uuid} is nearing expiration.", device.UUID);
                            await RenewLicenseAsync(device);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred during scheduled license renewals.");
                }

                await Task.Delay(TimeSpan.FromMinutes(_settings.LicenseCheckIntervalMinutes), stoppingToken);
            }

            _logger.LogInformation("Scheduled license renewals are stopping.");
        }
    }
}
