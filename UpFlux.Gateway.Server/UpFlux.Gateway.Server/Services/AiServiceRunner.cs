using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpFlux.Gateway.Server.Models;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Manages the execution of the Python AI service script.
    /// </summary>
    public class AiServiceRunner
    {
        private readonly ILogger<AiServiceRunner> _logger;
        private readonly string _scriptPath;
        private Process _process;

        /// <summary>
        /// Initializes a new instance of the <see cref="AiServiceRunner"/> class.
        /// </summary>
        public AiServiceRunner(IOptions<GatewaySettings> settings, ILogger<AiServiceRunner> logger)
        {
            _logger = logger;
            _scriptPath = settings.Value.AiServiceScriptPath;
        }

        /// <summary>
        /// Starts the AI service Python script.
        /// </summary>
        public void StartAiService()
        {
            try
            {
                _logger.LogInformation("Starting AI Service Python script: {ScriptPath}", _scriptPath);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"-u {_scriptPath}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _process = new Process { StartInfo = startInfo };

                _process.OutputDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        _logger.LogInformation("AI Service Output: {Data}", args.Data);
                    }
                };

                _process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        _logger.LogError("AI Service Error: {Error}", args.Data);
                    }
                };

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                _logger.LogInformation("AI Service started successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start AI Service.");
            }
        }

        /// <summary>
        /// Stops the AI service script.
        /// </summary>
        public void StopAiService()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _logger.LogInformation("AI Service stopped.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping AI Service.");
            }
        }
    }
}
