using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Repositories;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for managing software updates received from the cloud and distributing them to devices.
    /// </summary>
    public class UpdateManagementService
    {
        private readonly ILogger<UpdateManagementService> _logger;
        private readonly GatewaySettings _settings;
        private readonly DeviceCommunicationService _deviceCommunicationService;
        private readonly string _updateStoragePath;

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateManagementService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="settings">Gateway settings.</param>
        /// <param name="deviceCommunicationService">Service for communication with devices.</param>
        public UpdateManagementService(
            ILogger<UpdateManagementService> logger,
            IOptions<GatewaySettings> settings,
            DeviceCommunicationService deviceCommunicationService)
        {
            _logger = logger;
            _settings = settings.Value;
            _deviceCommunicationService = deviceCommunicationService;
            _updateStoragePath = Path.Combine(AppContext.BaseDirectory, "Updates");

            // Ensure the update storage directory exists
            Directory.CreateDirectory(_updateStoragePath);
        }

        /// <summary>
        /// Handles a new update package received from the cloud.
        /// </summary>
        /// <param name="updatePackage">The update package to process.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleUpdatePackageAsync(UpdatePackage updatePackage)
        {
            _logger.LogInformation("Processing update package file '{file}' for devices: {devices}",
                           updatePackage.FileName,
                           string.Join(", ", updatePackage.TargetDevices ?? Array.Empty<string>()));

            try
            {
                // Save the update package file
                string filePath = Path.Combine(_updateStoragePath, updatePackage.FileName);
                File.Copy(updatePackage.FilePath, filePath, overwrite: true);
                updatePackage.FilePath = filePath;

                // Distribute the update to devices
                await DistributeUpdateAsync(updatePackage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing update package '{file}'", updatePackage.FileName);
            }
        }

        /// <summary>
        /// Distributes the update package to the target devices.
        /// </summary>
        /// <param name="updatePackage">The update package to distribute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task DistributeUpdateAsync(UpdatePackage updatePackage)
        {
            _logger.LogInformation("Distributing update '{file}' to devices...", updatePackage.FileName);

            List<Task> tasks = new List<Task>();

            foreach (string deviceUuid in updatePackage.TargetDevices)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("Sending update package '{file}' to device {deviceUuid}",
                                       updatePackage.FileName, deviceUuid);

                        // Send the update package to the device
                        bool success = await _deviceCommunicationService.SendUpdatePackageAsync(deviceUuid, updatePackage.FilePath);

                        if (success)
                        {
                            _logger.LogInformation("Package '{file}' successfully sent to device {deviceUuid}.",
                                           updatePackage.FileName, deviceUuid);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to send package '{file}' to device {deviceUuid}.",
                                        updatePackage.FileName, deviceUuid);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending package '{file}' to device {deviceUuid}.",
                                 updatePackage.FileName, deviceUuid);
                    }
                }));
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);

            _logger.LogInformation("Distribution of '{file}' completed.", updatePackage.FileName);
        }
    }
}
