using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Repositories;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for managing software versions on devices.
    /// </summary>
    public class VersionControlService
    {
        private readonly ILogger<VersionControlService> _logger;
        private readonly DeviceRepository _deviceRepository;
        private readonly VersionRepository _versionRepository;
        private readonly DeviceCommunicationService _deviceCommunicationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionControlService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="deviceRepository">Device repository instance.</param>
        /// <param name="versionRepository">Version repository instance.</param>
        /// <param name="deviceCommunicationService">Device communication service instance.</param>
        public VersionControlService(
            ILogger<VersionControlService> logger,
            DeviceRepository deviceRepository,
            VersionRepository versionRepository,
            DeviceCommunicationService deviceCommunicationService)
        {
            _logger = logger;
            _deviceRepository = deviceRepository;
            _versionRepository = versionRepository;
            _deviceCommunicationService = deviceCommunicationService;
        }

        /// <summary>
        /// Retrieves current software versions from all devices.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task RetrieveVersionsAsync()
        {
            _logger.LogInformation("Starting version retrieval from devices.");

            List<Device> devices = _deviceRepository.GetAllDevices();

            List<Task> tasks = new List<Task>();

            foreach (Device device in devices)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("Requesting versions from device {uuid}", device.UUID);

                        List<VersionInfo> versionInfoList = await _deviceCommunicationService.RequestVersionInfoAsync(device);

                        if (versionInfoList != null && versionInfoList.Count > 0)
                        {
                            _logger.LogInformation("Received {count} versions from device {uuid}", versionInfoList.Count, device.UUID);

                            // Store version data in the database
                            foreach (VersionInfo versionInfo in versionInfoList)
                            {
                                _versionRepository.AddVersionInfo(versionInfo);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Failed to retrieve versions from device {uuid}", device.UUID);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error retrieving versions from device {uuid}", device.UUID);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            _logger.LogInformation("Version retrieval completed.");
        }

        /// <summary>
        /// Retrieves all stored version information for transmission.
        /// </summary>
        /// <returns>A list of VersionInfo objects.</returns>
        public List<VersionInfo> GetAllVersionInfo()
        {
            return _versionRepository.GetAllVersionInfo();
        }
    }
}
