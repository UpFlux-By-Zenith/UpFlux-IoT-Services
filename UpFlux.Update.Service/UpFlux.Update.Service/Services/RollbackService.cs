using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UpFlux.Update.Service.Models;

namespace UpFlux.Update.Service.Services
{
    /// <summary>
    /// Handles rolling back to the previous version if errors are detected.
    /// </summary>
    public class RollbackService
    {
        private readonly ILogger<RollbackService> _logger;
        private readonly InstallationService _installationService;

        public RollbackService(ILogger<RollbackService> logger, InstallationService installationService)
        {
            _logger = logger;
            _installationService = installationService;
        }

        /// <summary>
        /// Rolls back to the specified previous package.
        /// </summary>
        public async Task RollbackAsync(UpdatePackage previousPackage)
        {
            _logger.LogInformation("Starting rollback to previous version.");

            // Uninstall current version
            bool uninstallResult = await UninstallCurrentVersionAsync();
            if (!uninstallResult)
            {
                _logger.LogError("Rollback failed during uninstallation.");
                return;
            }

            // Install previous version
            bool installResult = await _installationService.InstallPackageAsync(previousPackage);
            if (!installResult)
            {
                _logger.LogError("Rollback failed during installation of previous version.");
                return;
            }

            _logger.LogInformation("Rollback successful.");
        }

        private async Task<bool> UninstallCurrentVersionAsync()
        {
            _logger.LogInformation("Uninstalling current version.");

            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = $"apt purge -y upflux-monitoring-service",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            Process process = new Process { StartInfo = processStartInfo };

            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            process.WaitForExit();

            _logger.LogInformation("Uninstallation output: " + output);

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Uninstallation successful.");
                return true;
            }
            else
            {
                _logger.LogError("Uninstallation failed: " + error);
                return false;
            }
        }
    }
}
