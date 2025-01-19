using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using UpFlux.Update.Service.Models;
using UpFlux.Update.Service.Utilities;

namespace UpFlux.Update.Service.Services
{
    public class TcpListenerService
    {
        private readonly ILogger<TcpListenerService> _logger;
        private readonly Configuration _config;
        private TcpListener _tcpListener;
        private bool _isListening;
        private readonly RollbackService _rollbackService;
        private readonly VersionManager _versionManager;

        public TcpListenerService(ILogger<TcpListenerService> logger, IOptions<Configuration> configOptions, RollbackService rollbackService,VersionManager versionManager)
        {
            _logger = logger;
            _config = configOptions.Value;

            // Ensure the incoming directory exists
            Directory.CreateDirectory(_config.IncomingPackageDirectory);
            _rollbackService = rollbackService;
            _versionManager = versionManager;
        }

        public void StartListening(int port)
        {
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _tcpListener.Start();
            _isListening = true;
            Task.Run(() => ListenForConnections());
        }

        private async Task ListenForConnections()
        {
            _logger.LogInformation("TCP Listener started.");
            while (_isListening)
            {
                try
                {
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error accepting TCP client.");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                using NetworkStream networkStream = client.GetStream();

                _logger.LogInformation("Connection established with the Gateway Server.");

                // Send Device UUID to the Gateway Server
                string uuidMessage = $"UUID:{_config.DeviceUuid}\n";
                byte[] uuidBytes = Encoding.UTF8.GetBytes(uuidMessage);
                await networkStream.WriteAsync(uuidBytes, 0, uuidBytes.Length);
                await networkStream.FlushAsync();

                _logger.LogInformation("Device UUID sent to Gateway Server: {uuid}", _config.DeviceUuid);

                // Receive commands from the Gateway Server
                await ReceiveCommandsAsync(networkStream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling TCP client.");
            }
            finally
            {
                client.Close();
            }
        }

        private async Task ReceiveCommandsAsync(NetworkStream networkStream)
        {
            try
            {
                while (true)
                {
                    string command = await ReadMessageAsync(networkStream);
                    if (string.IsNullOrEmpty(command))
                    {
                        _logger.LogInformation("No command received. Closing connection.");
                        break;
                    }

                    _logger.LogInformation("Received command: {command}", command);

                    if (command.StartsWith("SEND_PACKAGE"))
                    {
                        // Handle receiving the package
                        await ReceivePackageAsync(networkStream, command);
                    }
                    else if (command.StartsWith("LICENSE"))
                    {
                        // Handle the license
                        string license = command.Substring("LICENSE:".Length).Trim();
                        StoreLicense(license);
                    }
                    else if (command == "REQUEST_LOGS")
                    {
                        // Send logs to the Gateway Server
                        await SendLogsAsync(networkStream);
                    }
                    else if (command.StartsWith("ROLLBACK:"))
                    {
                        // Handle rollback command
                        string version = command.Substring("ROLLBACK:".Length).Trim();
                        await HandleRollbackCommandAsync(networkStream, version);
                    }
                    else if (command == "GET_VERSIONS")
                    {
                        // Handle GET_VERSIONS command
                        await HandleGetVersionsCommandAsync(networkStream);
                    }
                    else
                    {
                        _logger.LogWarning("Unknown command received: {command}", command);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving commands from Gateway Server.");
            }
        }

        private async Task ReceivePackageAsync(NetworkStream networkStream, string command)
        {
            try
            {
                // Expected format: SEND_PACKAGE:<filename>
                string[] parts = command.Split(':');
                if (parts.Length != 2)
                {
                    _logger.LogWarning("Invalid SEND_PACKAGE command format.");
                    return;
                }

                string fileName = parts[1];

                // Validate the filename
                if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    _logger.LogError("Invalid filename received.");
                    return;
                }

                // Generate the destination path
                string destinationPath = Path.Combine(_config.IncomingPackageDirectory, fileName);

                // Send acknowledgment
                string ackMessage = "READY_FOR_PACKAGE\n";
                byte[] ackBytes = Encoding.UTF8.GetBytes(ackMessage);
                await networkStream.WriteAsync(ackBytes, 0, ackBytes.Length);
                await networkStream.FlushAsync();

                // Read the length of the package (as 4-byte integer)
                byte[] lengthBytes = new byte[4];
                int totalBytesRead = 0;
                while (totalBytesRead < 4)
                {
                    int bytesRead = await networkStream.ReadAsync(lengthBytes, totalBytesRead, 4 - totalBytesRead);
                    if (bytesRead == 0)
                    {
                        throw new Exception("Gateway Server closed the connection unexpectedly.");
                    }
                    totalBytesRead += bytesRead;
                }

                int packageLength = BitConverter.ToInt32(lengthBytes, 0);

                _logger.LogInformation("Receiving package '{fileName}' of length {length} bytes.", fileName, packageLength);

                // Read the package data
                byte[] packageData = new byte[packageLength];
                totalBytesRead = 0;
                while (totalBytesRead < packageLength)
                {
                    int bytesRead = await networkStream.ReadAsync(packageData, totalBytesRead, packageLength - totalBytesRead);
                    if (bytesRead == 0)
                    {
                        throw new Exception("Gateway Server closed the connection unexpectedly.");
                    }
                    totalBytesRead += bytesRead;
                }

                // Save the package data to a file
                await File.WriteAllBytesAsync(destinationPath, packageData);

                _logger.LogInformation("Package '{fileName}' received and saved to '{path}'.", fileName, destinationPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving package from Gateway Server.");
            }
        }

        // Helper method to read a message terminated by a newline character
        private async Task<string> ReadMessageAsync(NetworkStream networkStream)
        {
            StringBuilder messageData = new StringBuilder();
            byte[] buffer = new byte[1024];
            int bytesRead = -1;

            do
            {
                bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break;
                }

                string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageData.Append(chunk);

                if (chunk.Contains("\n"))
                {
                    break;
                }
            } while (bytesRead != 0);

            return messageData.ToString().Trim();
        }

        private void StoreLicense(string license)
        {
            try
            {
                string licensePath = _config.LicenseFilePath;

                // Ensure the directory exists
                string directory = Path.GetDirectoryName(licensePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write the license to the file securely
                File.WriteAllText(licensePath, license);

                // Set appropriate permissions
                FileInfo fileInfo = new FileInfo(licensePath);
                fileInfo.Attributes = FileAttributes.ReadOnly;

                _logger.LogInformation("License stored securely at {path}", licensePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store license.");
            }
        }

        public void StopListening()
        {
            _isListening = false;
            _tcpListener.Stop();
        }

        /// <summary>
        /// Method to send logs to the Gateway Server over the secure connection.
        /// </summary>
        /// <param name="sslStream">The SSL stream to send logs over.</param>
        /// <returns> Returns a Task representing the asynchronous operation.</returns>
        private async Task SendLogsAsync(NetworkStream networkStream)
        {
            try
            {
                string updateServiceLogPath = _config.UpdateServiceLog;
                string monitoringServiceLogPath = _config.MonitoringServiceLog;

                if (!File.Exists(updateServiceLogPath) && !File.Exists(monitoringServiceLogPath))
                {
                    _logger.LogWarning("No log files found to send.");
                    return;
                }

                // Prepare a list of logs to send
                var logsToSend = new List<(string FileName, string FilePath)>();

                if (File.Exists(updateServiceLogPath))
                {
                    logsToSend.Add(("UpdateServiceLog.log", updateServiceLogPath));
                }

                if (File.Exists(monitoringServiceLogPath))
                {
                    logsToSend.Add(("MonitoringServiceLog.log", monitoringServiceLogPath));
                }

                // Send the number of log files
                byte[] fileCountBytes = BitConverter.GetBytes(logsToSend.Count);
                await networkStream.WriteAsync(fileCountBytes, 0, fileCountBytes.Length);
                await networkStream.FlushAsync();

                foreach (var log in logsToSend)
                {
                    // Send the file name length and file name
                    byte[] fileNameBytes = Encoding.UTF8.GetBytes(log.FileName);
                    byte[] fileNameLengthBytes = BitConverter.GetBytes(fileNameBytes.Length);
                    await networkStream.WriteAsync(fileNameLengthBytes, 0, fileNameLengthBytes.Length);
                    await networkStream.WriteAsync(fileNameBytes, 0, fileNameBytes.Length);
                    await networkStream.FlushAsync();

                    // Read the file data
                    byte[] logBytes = File.ReadAllBytes(log.FilePath);
                    int logLength = logBytes.Length;

                    // Send the length of the log file (as 4-byte integer)
                    byte[] lengthBytes = BitConverter.GetBytes(logLength);
                    await networkStream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await networkStream.FlushAsync();

                    // Send the log file data
                    await networkStream.WriteAsync(logBytes, 0, logBytes.Length);
                    await networkStream.FlushAsync();
                }

                _logger.LogInformation("Logs sent to Gateway Server successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send logs to Gateway Server.");
            }
        }

        /// <summary>
        /// This method sends a notification message to the Gateway Server.
        /// </summary>
        /// <param name="message">The notification message to send.</param>
        /// <returns>Returns a Task representing the asynchronous operation.</returns>
        public async Task SendNotificationAsync(string message)
        {
            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(_config.GatewayServerIp, _config.GatewayServerPort);

                using NetworkStream networkStream = client.GetStream();

                _logger.LogInformation("Connection established with the Gateway Server for notification.");

                // Send Device UUID to the Gateway Server
                string uuidMessage = $"{_config.DeviceUuid}\n";
                byte[] uuidBytes = Encoding.UTF8.GetBytes(uuidMessage);
                await networkStream.WriteAsync(uuidBytes, 0, uuidBytes.Length);
                await networkStream.FlushAsync();

                // Send the notification message
                string notificationMessage = $"NOTIFICATION:{message}\n";
                byte[] messageBytes = Encoding.UTF8.GetBytes(notificationMessage);
                await networkStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                await networkStream.FlushAsync();

                _logger.LogInformation("Notification sent to Gateway Server: {message}", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notification to Gateway Server.");
            }
        }

        private async Task HandleRollbackCommandAsync(NetworkStream networkStream, string version)
        {
            try
            {
                _logger.LogInformation("Initiating rollback to version {version}", version);

                // Send acknowledgment
                string ackMessage = "ROLLBACK_INITIATED\n";
                byte[] ackBytes = Encoding.UTF8.GetBytes(ackMessage);
                await networkStream.WriteAsync(ackBytes, 0, ackBytes.Length);
                await networkStream.FlushAsync();

                // Perform rollback
                bool rollbackResult = await _rollbackService.ManualRollbackAsync(version);

                if (rollbackResult)
                {
                    _logger.LogInformation("Rollback to version {version} completed successfully.", version);

                    // Send confirmation
                    string confirmationMessage = "ROLLBACK_COMPLETED\n";
                    byte[] confirmationBytes = Encoding.UTF8.GetBytes(confirmationMessage);
                    await networkStream.WriteAsync(confirmationBytes, 0, confirmationBytes.Length);
                    await networkStream.FlushAsync();

                    // Send a notification back to the Gateway Server
                    await SendNotificationAsync($"Rollback to version {version} completed successfully.");
                }
                else
                {
                    _logger.LogError("Rollback to version {version} failed.", version);

                    // Send failure message
                    string failureMessage = "ROLLBACK_FAILED\n";
                    byte[] failureBytes = Encoding.UTF8.GetBytes(failureMessage);
                    await networkStream.WriteAsync(failureBytes, 0, failureBytes.Length);
                    await networkStream.FlushAsync();

                    // Send a notification back to the Gateway Server
                    await SendNotificationAsync($"Rollback to version {version} failed.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during rollback to version {version}.", version);

                // Send failure message
                string failureMessage = "ROLLBACK_FAILED\n";
                byte[] failureBytes = Encoding.UTF8.GetBytes(failureMessage);
                await networkStream.WriteAsync(failureBytes, 0, failureBytes.Length);
                await networkStream.FlushAsync();
            }
        }

        private async Task HandleGetVersionsCommandAsync(NetworkStream networkStream)
        {
            try
            {
                _logger.LogInformation("Handling GET_VERSIONS command.");

                // Get the current installed version and list of available versions
                object versionInfo = _versionManager.GetVersionInfo();

                // Serialize versionInfo to JSON
                string versionInfoJson = JsonConvert.SerializeObject(versionInfo);

                // Send the JSON string to the Gateway Server
                byte[] versionInfoBytes = Encoding.UTF8.GetBytes(versionInfoJson + "\n");
                await networkStream.WriteAsync(versionInfoBytes, 0, versionInfoBytes.Length);
                await networkStream.FlushAsync();

                _logger.LogInformation("Version information sent to Gateway Server.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling GET_VERSIONS command.");
            }
        }

    }
}
