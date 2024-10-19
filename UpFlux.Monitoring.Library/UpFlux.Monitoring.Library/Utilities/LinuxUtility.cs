using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Library.Utilities
{
    /// <summary>
    /// Utility class to run Linux commands and return the result as a string.
    /// </summary>
    public static class LinuxUtility
    {
        /// <summary>
        /// Default timeout for command execution (30 seconds)
        /// </summary>
        private const int DefaultTimeoutMilliseconds = 30000;

        /// <summary>
        /// Runs a Linux command and returns the output as a string.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        /// <param name="timeoutMilliseconds">Timeout for the command execution in milliseconds. The Default is 30 seconds.</param>
        /// <returns>The output of the command as a string.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the command fails or times out.</exception>
        public static string RunCommand(string command, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            try
            {
                using (Process process = new Process())
                {
                    // Set up process start info
                    process.StartInfo.FileName = "/bin/bash";
                    process.StartInfo.Arguments = $"-c \"{command}\"";
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                    process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

                    // Start process
                    process.Start();

                    // Use cancellation token to wait for a timeout
                    StringBuilder outputBuilder = new StringBuilder();
                    StringBuilder errorBuilder = new StringBuilder();

                    // reading asynchronously from stdout and stderr
                    using (var outputWaitHandle = new AutoResetEvent(false))
                    using (var errorWaitHandle = new AutoResetEvent(false))
                    {
                        // Read output stream
                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (e.Data == null)
                                outputWaitHandle.Set();
                            else
                                outputBuilder.AppendLine(e.Data);
                        };
                        process.BeginOutputReadLine();

                        // Read error stream
                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (e.Data == null)
                                errorWaitHandle.Set();
                            else
                                errorBuilder.AppendLine(e.Data);
                        };
                        process.BeginErrorReadLine();

                        // Wait for the process to complete or timeout
                        if (process.WaitForExit(timeoutMilliseconds) &&
                            outputWaitHandle.WaitOne(timeoutMilliseconds) &&
                            errorWaitHandle.WaitOne(timeoutMilliseconds))
                        {
                            // If the process has exited, check the exit code and throw an exception if it is non-zero
                            if (process.ExitCode != 0)
                            {
                                string errorOutput = errorBuilder.ToString();
                                throw new InvalidOperationException(
                                    $"Command '{command}' failed with exit code {process.ExitCode}: {errorOutput}");
                            }

                            // Return output
                            return outputBuilder.ToString();
                        }
                        else
                        {
                            // Timeout occurred
                            process.Kill();
                            throw new TimeoutException($"Command '{command}' timed out after {timeoutMilliseconds} ms.");
                        }
                    }
                }
            }
            catch (TimeoutException ex)
            {
                throw new InvalidOperationException($"Command execution timed out: {command}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error running command: {command}", ex);
            }
        }
    }
}
