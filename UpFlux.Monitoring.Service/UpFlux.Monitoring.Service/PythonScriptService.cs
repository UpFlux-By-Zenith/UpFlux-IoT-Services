using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UpFlux.Monitoring.Service
{
    /// <summary>
    /// Manages the execution of the Python sensor script.
    /// </summary>
    public class PythonScriptService
    {
        private readonly string _scriptPath;
        private readonly ILogger<PythonScriptService> _logger;
        private Process _process;
        private string _latestSensorData;

        /// <summary>
        /// Initializes a new instance of the <see cref="PythonScriptService"/> class.
        /// </summary>
        public PythonScriptService(IOptions<ServiceSettings> settings, ILogger<PythonScriptService> logger)
        {
            _logger = logger;
            _latestSensorData = string.Empty;

            // Use the script path directly from settings
            _scriptPath = settings.Value.SensorScriptPath;
        }

        /// <summary>
        /// Starts the Python script and captures its output.
        /// </summary>
        public void StartPythonScript()
        {
            try
            {
                _logger.LogInformation("Starting Python script: {ScriptPath}", _scriptPath);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"-u {_scriptPath}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Set the working directory to the application's base directory
                startInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;

                _process = new Process { StartInfo = startInfo };

                // Event handler for standard output
                _process.OutputDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        _latestSensorData = args.Data;
                        //_logger.LogInformation("Sensor data received: {Data}", args.Data);
                    }
                };

                // Event handler for standard error
                _process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        _logger.LogError("Python script error: {Error}", args.Data);
                    }
                };

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                _logger.LogInformation("Python script started successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Python script.");
            }
        }

        /// <summary>
        /// Stops the Python script.
        /// </summary>
        public void StopPythonScript()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _logger.LogInformation("Python script stopped.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Python script.");
            }
        }

        /// <summary>
        /// Gets the latest sensor data captured from the Python script.
        /// </summary>
        public string GetLatestSensorData()
        {
            return _latestSensorData;
        }
    }
}
