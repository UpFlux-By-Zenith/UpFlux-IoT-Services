using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpFlux.Update.Service.Models;
using UpFlux.Update.Service.Utilities;

namespace UpFlux.Update.Service.Services
{
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

        public UpdateService(
            ILogger<UpdateService> logger,
            IOptions<Configuration> configOptions,
            TcpListenerService tcpListenerService,
            FileWatcherService fileWatcherService,
            VersionManager versionManager,
            SimulationService simulationService,
            InstallationService installationService,
            LogMonitoringService logMonitoringService,
            RollbackService rollbackService)
        {
            _logger = logger;
            _tcpListenerService = tcpListenerService;
            _fileWatcherService = fileWatcherService;
            _versionManager = versionManager;
            _simulationService = simulationService;
            _installationService = installationService;
            _logMonitoringService = logMonitoringService;
            _rollbackService = rollbackService;
            _config = configOptions.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("UpFlux Update Service is starting.");

            // Start the TCP listener
            _tcpListenerService.StartListening(_config.DeviceServerPort);

            // Start the file system watcher
            _fileWatcherService.PackageDetected += async (sender, package) => await HandlePackageAsync(package);
            _fileWatcherService.StartWatching();

            await Task.CompletedTask;
        }

        private async Task HandlePackageAsync(UpdatePackage package)
        {
            try
            {
                if (!IsMonitoringServicePackage(package))
                {
                    _logger.LogWarning("Received package is not the UpFlux Monitoring Service package. Ignoring.");
                    return;
                }

                // Store the package (clean old versions)
                _versionManager.StorePackage(package);

                // Perform simulation
                bool simulationResult = await _simulationService.SimulateInstallationAsync(package);
                if (!simulationResult)
                {
                    _logger.LogError("Simulation failed. Aborting update.");
                    await _tcpListenerService.SendNotificationAsync("Simulation failed for version " + package.Version);
                    return;
                }

                // Install the package
                bool installationResult = await _installationService.InstallPackageAsync(package);
                if (!installationResult)
                {
                    _logger.LogError("Installation failed. Aborting update.");
                    await _tcpListenerService.SendNotificationAsync("Installation failed for version " + package.Version);
                    return;
                }

                // Monitor the monitoring service logs
                bool monitoringResult = await _logMonitoringService.MonitorLogsAsync();
                if (!monitoringResult)
                {
                    _logger.LogError("Errors detected after installation. Initiating rollback.");
                    await _tcpListenerService.SendNotificationAsync("Errors detected after installation of version " + package.Version);

                    UpdatePackage previousPackage = _versionManager.GetPreviousVersion(package.Version);
                    if (previousPackage != null)
                    {
                        await _rollbackService.RollbackAsync(previousPackage);
                        await _tcpListenerService.SendNotificationAsync("Rolled back to version " + previousPackage.Version);
                    }
                    else
                    {
                        _logger.LogError("No previous version available for rollback.");
                        await _tcpListenerService.SendNotificationAsync("No previous version available for rollback.");
                    }
                }
                else
                {
                    _logger.LogInformation("Update installed successfully and is running without errors.");
                    await _tcpListenerService.SendNotificationAsync("Update to version " + package.Version + " installed successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during the update process.");
                await _tcpListenerService.SendNotificationAsync("An error occurred during the update process: " + ex.Message);
            }
        }

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