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
    /// Handles the installation of packages.
    /// </summary>
    public class InstallationService
    {
        private readonly ILogger<InstallationService> _logger;

        public InstallationService(ILogger<InstallationService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Installs the package using apt.
        /// </summary>
        public async Task<bool> InstallPackageAsync(UpdatePackage package)
        {
            _logger.LogInformation("Starting installation of package.");

            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = $"apt install -y \"{package.FilePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            Process process = new Process { StartInfo = processStartInfo };

            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            process.WaitForExit();

            _logger.LogInformation("Installation output: " + output);

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Installation successful.");
                return true;
            }
            else
            {
                _logger.LogError("Installation failed: " + error);
                return false;
            }
        }
    }
}
