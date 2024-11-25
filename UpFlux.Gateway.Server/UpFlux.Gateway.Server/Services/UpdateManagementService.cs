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
        private readonly ConcurrentDictionary<string, UpdateStatus> _updateStatuses;
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
            _updateStatuses = new ConcurrentDictionary<string, UpdateStatus>();
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
            _logger.LogInformation("Processing update package {packageId} version {version}", updatePackage.PackageId, updatePackage.Version);

            try
            {
                // Verify the digital signature
                if (!VerifyUpdatePackage(updatePackage))
                {
                    _logger.LogWarning("Update package {packageId} failed signature verification.", updatePackage.PackageId);
                    return;
                }

                // Save the update package file
                string filePath = Path.Combine(_updateStoragePath, updatePackage.FileName);
                File.WriteAllBytes(filePath, File.ReadAllBytes(updatePackage.FilePath));
                updatePackage.FilePath = filePath;

                // Initialize update status
                UpdateStatus updateStatus = new UpdateStatus
                {
                    PackageId = updatePackage.PackageId,
                    Version = updatePackage.Version,
                    TargetDevices = updatePackage.TargetDevices,
                    DevicesPending = new HashSet<string>(updatePackage.TargetDevices),
                    DevicesSucceeded = new HashSet<string>(),
                    DevicesFailed = new HashSet<string>()
                };

                _updateStatuses[updatePackage.PackageId] = updateStatus;

                // Distribute the update to devices
                await DistributeUpdateAsync(updatePackage, updateStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing update package {packageId}", updatePackage.PackageId);
            }
        }

        /// <summary>
        /// Verifies the authenticity of the update package using a digital signature.
        /// </summary>
        /// <param name="updatePackage">The update package to verify.</param>
        /// <returns>True if the update package is valid; otherwise, false.</returns>
        private bool VerifyUpdatePackage(UpdatePackage updatePackage)
        {
            try
            {
                // Load the public key from the settings or a file
                string publicKeyPath = _settings.UpdatePackagePublicKeyPath;
                using RSA rsa = RSA.Create();
                rsa.ImportFromPem(File.ReadAllText(publicKeyPath));

                // Read the update package file
                byte[] packageData = File.ReadAllBytes(updatePackage.FilePath);

                // Verify the signature
                bool isValid = rsa.VerifyData(packageData, updatePackage.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                if (!isValid)
                {
                    _logger.LogWarning("Digital signature verification failed for update package {packageId}", updatePackage.PackageId);
                }

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during signature verification for update package {packageId}", updatePackage.PackageId);
                return false;
            }
        }

        /// <summary>
        /// Distributes the update package to the target devices.
        /// </summary>
        /// <param name="updatePackage">The update package to distribute.</param>
        /// <param name="updateStatus">The update status to track progress.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task DistributeUpdateAsync(UpdatePackage updatePackage, UpdateStatus updateStatus)
        {
            _logger.LogInformation("Distributing update package {packageId} to devices.", updatePackage.PackageId);

            List<Task> tasks = new List<Task>();

            foreach (string deviceUuid in updatePackage.TargetDevices)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("Sending update package {packageId} to device {deviceUuid}", updatePackage.PackageId, deviceUuid);

                        // Send the update package to the device
                        bool success = await _deviceCommunicationService.SendUpdatePackageAsync(deviceUuid, updatePackage.FilePath);

                        if (success)
                        {
                            _logger.LogInformation("Update package {packageId} successfully sent to device {deviceUuid}", updatePackage.PackageId, deviceUuid);
                            updateStatus.DevicesSucceeded.Add(deviceUuid);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to send update package {packageId} to device {deviceUuid}", updatePackage.PackageId, deviceUuid);
                            updateStatus.DevicesFailed.Add(deviceUuid);
                        }

                        updateStatus.DevicesPending.Remove(deviceUuid);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending update package {packageId} to device {deviceUuid}", updatePackage.PackageId, deviceUuid);
                        updateStatus.DevicesFailed.Add(deviceUuid);
                        updateStatus.DevicesPending.Remove(deviceUuid);
                    }
                }));
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);

            _logger.LogInformation("Update package {packageId} distribution completed.", updatePackage.PackageId);

            // Handle retries for failed devices
            if (updateStatus.DevicesFailed.Count > 0)
            {
                _logger.LogInformation("Retrying update package {packageId} for failed devices.", updatePackage.PackageId);
                await RetryFailedDevicesAsync(updatePackage, updateStatus);
            }
        }

        /// <summary>
        /// Retries sending the update package to devices that previously failed.
        /// </summary>
        /// <param name="updatePackage">The update package to send.</param>
        /// <param name="updateStatus">The update status to track progress.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task RetryFailedDevicesAsync(UpdatePackage updatePackage, UpdateStatus updateStatus)
        {
            // Implement retry logic with exponential backoff
            int maxRetries = _settings.UpdateMaxRetries;
            int retryCount = 0;

            while (retryCount < maxRetries && updateStatus.DevicesFailed.Count > 0)
            {
                retryCount++;
                _logger.LogInformation("Retry attempt {retryCount} for update package {packageId}", retryCount, updatePackage.PackageId);

                List<string> failedDevices = new List<string>(updateStatus.DevicesFailed);
                updateStatus.DevicesFailed.Clear();

                List<Task> tasks = new List<Task>();

                foreach (string deviceUuid in failedDevices)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            _logger.LogInformation("Retrying update package {packageId} for device {deviceUuid}", updatePackage.PackageId, deviceUuid);

                            // Send the update package to the device
                            bool success = await _deviceCommunicationService.SendUpdatePackageAsync(deviceUuid, updatePackage.FilePath);

                            if (success)
                            {
                                _logger.LogInformation("Update package {packageId} successfully sent to device {deviceUuid}", updatePackage.PackageId, deviceUuid);
                                updateStatus.DevicesSucceeded.Add(deviceUuid);
                            }
                            else
                            {
                                _logger.LogWarning("Failed to send update package {packageId} to device {deviceUuid}", updatePackage.PackageId, deviceUuid);
                                updateStatus.DevicesFailed.Add(deviceUuid);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error retrying update package {packageId} for device {deviceUuid}", updatePackage.PackageId, deviceUuid);
                            updateStatus.DevicesFailed.Add(deviceUuid);
                        }
                    }));
                }

                // Wait for all tasks to complete
                await Task.WhenAll(tasks);

                // Wait before next retry attempt
                int delaySeconds = (int)Math.Pow(2, retryCount); // Exponential backoff
                _logger.LogInformation("Waiting {delaySeconds} seconds before next retry attempt.", delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }

            if (updateStatus.DevicesFailed.Count > 0)
            {
                _logger.LogWarning("Update package {packageId} failed to send to some devices after {maxRetries} retries.", updatePackage.PackageId, maxRetries);
            }
        }

        /// <summary>
        /// Gets the update status for a given update package.
        /// </summary>
        /// <param name="packageId">The package identifier.</param>
        /// <returns>The update status.</returns>
        public UpdateStatus GetUpdateStatus(string packageId)
        {
            _updateStatuses.TryGetValue(packageId, out UpdateStatus? status);
            return status;
        }
    }
}
