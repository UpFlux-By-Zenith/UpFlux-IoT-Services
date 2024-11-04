using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UpFlux.Update.Service.Models;
using Microsoft.Extensions.Options;

namespace UpFlux.Update.Service.Services
{
    /// <summary>
    /// Performs simulation runs of package installations.
    /// </summary>
    public class SimulationService
    {
        private readonly ILogger<SimulationService> _logger;
        private readonly Configuration _config;

        public SimulationService(ILogger<SimulationService> logger, IOptions<Configuration> configOptions)
        {
            _logger = logger;
            _config = configOptions.Value;
        }

        /// <summary>
        /// Simulates the installation of a package.
        /// </summary>
        public async Task<bool> SimulateInstallationAsync(UpdatePackage package)
        {
            _logger.LogInformation("Starting simulation of package installation.");

            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = $"dpkg -i --simulate \"{package.FilePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            Process process = new Process { StartInfo = processStartInfo };

            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            process.WaitForExit();

            _logger.LogInformation("Simulation output: " + output);

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Simulation successful.");
                return true;
            }
            else
            {
                _logger.LogError("Simulation failed: " + error);
                return false;
            }
        }
    }
}
