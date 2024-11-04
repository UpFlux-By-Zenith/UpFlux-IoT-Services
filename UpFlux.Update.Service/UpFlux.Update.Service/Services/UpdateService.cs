using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpFlux.Update.Service.Models;
using UpFlux.Update.Service.Utilities;

namespace UpFlux.Update.Service.Services
{
    /// <summary>
    /// Main service that orchestrates the update process.
    /// </summary>
    public class UpdateService : BackgroundService
    {
        private readonly ILogger<UpdateService> _logger;
        private readonly TcpListenerService _tcpListenerService;
        private readonly FileWatcherService _fileWatcherService;
        private readonly Configuration _config;
        private readonly VersionManager _versionManager;
        private readonly SimulationService _simulationService;
        private readonly InstallationService _installationService;
        private readonly LogMonitoringService _logMonitoringService;
        private readonly RollbackService _rollbackService;
        private readonly GatewayNotificationService _gatewayNotificationService;

        public UpdateService(
            ILogger<UpdateService> logger,
            IOptions<Configuration> configOptions,
            TcpListenerService tcpListenerService,
            FileWatcherService fileWatcherService,
            VersionManager versionManager,
            SimulationService simulationService,
            InstallationService installationService,
            LogMonitoringService logMonitoringService,
            RollbackService rollbackService,
            GatewayNotificationService gatewayNotificationService)
        {
            _logger = logger;
            _tcpListenerService = tcpListenerService;
            _fileWatcherService = fileWatcherService;
            _versionManager = versionManager;
            _simulationService = simulationService;
            _installationService = installationService;
            _logMonitoringService = logMonitoringService;
            _rollbackService = rollbackService;
            _gatewayNotificationService = gatewayNotificationService;
            _config = configOptions.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("UpFlux Update Service is starting.");

            // Start the TCP listener
            _tcpListenerService.PackageReceived += OnPackageReceived;
            _tcpListenerService.StartListening(_config.GatewayServerPort);

            // Start the file system watcher
            _fileWatcherService.PackageDetected += OnPackageDetected;
            _fileWatcherService.StartWatching(_config.PackageDirectory);

            await Task.CompletedTask;
        }

        private async void OnPackageReceived(object sender, UpdatePackage package)
        {
            _logger.LogInformation($"Package received: {package.FilePath}");
            await HandlePackageAsync(package);
        }

        private async void OnPackageDetected(object sender, UpdatePackage package)
        {
            _logger.LogInformation($"Package detected: {package.FilePath}");
            await HandlePackageAsync(package);
        }

        /// <summary>
        /// Handles the package update process.
        /// </summary>
        private async Task HandlePackageAsync(UpdatePackage package)
        {
            if (!IsMonitoringServicePackage(package))
            {
                _logger.LogWarning("Received package is not the UpFlux Monitoring Service package. Ignoring.");
                return;
            }

            // Store the package
            _versionManager.StorePackage(package);

            // Perform simulation
            bool simulationResult = await _simulationService.SimulateInstallationAsync(package);
            if (!simulationResult)
            {
                _logger.LogError("Simulation failed. Aborting update.");
                await _gatewayNotificationService.SendLogAsync("Simulation failed for version " + package.Version);
                return;
            }

            // Install the package
            bool installationResult = await _installationService.InstallPackageAsync(package);
            if (!installationResult)
            {
                _logger.LogError("Installation failed. Aborting update.");
                await _gatewayNotificationService.SendLogAsync("Installation failed for version " + package.Version);
                return;
            }

            // Monitor the monitoring service logs
            bool monitoringResult = await _logMonitoringService.MonitorLogsAsync();
            if (!monitoringResult)
            {
                _logger.LogError("Errors detected after installation. Initiating rollback.");
                await _gatewayNotificationService.SendLogAsync("Errors detected after installation of version " + package.Version);

                UpdatePackage previousPackage = _versionManager.GetPreviousVersion(package.Version);
                if (previousPackage != null)
                {
                    await _rollbackService.RollbackAsync(previousPackage);
                    await _gatewayNotificationService.SendLogAsync("Rolled back to version " + previousPackage.Version);
                }
                else
                {
                    _logger.LogError("No previous version available for rollback.");
                    await _gatewayNotificationService.SendLogAsync("No previous version available for rollback.");
                }
            }
            else
            {
                _logger.LogInformation("Update installed successfully and is running without errors.");
                await _gatewayNotificationService.SendLogAsync("Update to version " + package.Version + " installed successfully.");
            }
        }

        /// <summary>
        /// Checks if the package is the UpFlux Monitoring Service package.
        /// </summary>
        private bool IsMonitoringServicePackage(UpdatePackage package)
        {
            string fileName = Path.GetFileName(package.FilePath);
            return fileName.StartsWith("upflux-monitoring-service_") && fileName.EndsWith(".deb");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("UpFlux Update Service is stopping.");

            _tcpListenerService.StopListening();
            _fileWatcherService.StopWatching();

            await base.StopAsync(cancellationToken);
        }
    }
}
