using System;
using System.IO;
using System.IO.Compression;
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

                string command = await ReadMessageAsync(networkStream);
                if (string.IsNullOrEmpty(command))
                {
                    _logger.LogInformation("No command received. Closing connection.");
                    return;
                }

                _logger.LogInformation("Received command: {command}", command);

                if (command.StartsWith("SEND_PACKAGE"))
                {
                    await ReceivePackageAsync(networkStream, command);
                }
                else if (command.StartsWith("LICENSE"))
                {
                    string license = command.Substring("LICENSE:".Length).Trim();
                    StoreLicense(license);
                }
                else if (command == "REQUEST_LOGS")
                {
                    await SendLogsAsync(networkStream);
                }
                else if (command.StartsWith("ROLLBACK:"))
                {
                    string version = command.Substring("ROLLBACK:".Length).Trim();
                    await HandleRollbackCommandAsync(networkStream, version);
                }
                else if (command == "GET_VERSIONS")
                {
                    await HandleGetVersionsCommandAsync(networkStream);
                }
                else
                {
                    _logger.LogWarning("Unknown command received: {command}", command);
                }

                _logger.LogInformation("Done handling {command}. Closing connection.", command);
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

                // write to .partial in the incoming directory
                string finalPath = Path.Combine(_config.IncomingPackageDirectory, fileName);
                string partialPath = finalPath + ".partial";

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
                await File.WriteAllBytesAsync(partialPath, packageData);

                // rename the .partial to the final .deb
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }
                File.Move(partialPath, finalPath);

                _logger.LogInformation("Package '{fileName}' fully written and renamed to '{finalPath}'.", fileName, finalPath);
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
                string logsDir = _config.UpfluxLogPath;

                if (!Directory.Exists(logsDir))
                {
                    _logger.LogWarning("Logs directory '{dir}' does not exist. Sending fileCount=0.", logsDir);

                    // Send fileCount=0
                    byte[] zeroCount = BitConverter.GetBytes(0);
                    await networkStream.WriteAsync(zeroCount, 0, zeroCount.Length);
                    await networkStream.FlushAsync();
                    _logger.LogInformation("Logs directory not found. Sent fileCount=0.");
                    return;
                }

                string zipName = $"upflux-logs.zip";
                string zipFilePath = Path.Combine("/tmp", zipName);

                // Delete the old zip incase it exists
                if (File.Exists(zipFilePath))
                {
                    File.Delete(zipFilePath);
                }

                ZipFile.CreateFromDirectory(logsDir, zipFilePath);

                int fileCount = 1;
                byte[] fileCountBytes = BitConverter.GetBytes(fileCount);
                await networkStream.WriteAsync(fileCountBytes, 0, fileCountBytes.Length);
                await networkStream.FlushAsync();

                // Prepare the zip file name
                byte[] fileNameBytes = Encoding.UTF8.GetBytes(zipName);
                byte[] fileNameLenBytes = BitConverter.GetBytes(fileNameBytes.Length);
                await networkStream.WriteAsync(fileNameLenBytes, 0, fileNameLenBytes.Length);
                await networkStream.WriteAsync(fileNameBytes, 0, fileNameBytes.Length);
                await networkStream.FlushAsync();

                // Read the zip data and send
                byte[] zipData = File.ReadAllBytes(zipFilePath);
                int zipLength = zipData.Length;
                byte[] zipLenBytes = BitConverter.GetBytes(zipLength);
                await networkStream.WriteAsync(zipLenBytes, 0, zipLenBytes.Length);

                // Write the actual zip file
                await networkStream.WriteAsync(zipData, 0, zipData.Length);
                await networkStream.FlushAsync();

                _logger.LogInformation("Logs from '{logsDir}' zipped to '{zipFilePath}' and sent to Gateway.", logsDir, zipFilePath);
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
                string uuidMessage = $"UUID:{_config.DeviceUuid}\n";
                byte[] uuidBytes = Encoding.UTF8.GetBytes(uuidMessage);
                await networkStream.WriteAsync(uuidBytes, 0, uuidBytes.Length);
                await networkStream.FlushAsync();

                // Wait for a short time before sending so that the server can process the UUID
                await Task.Delay(100);

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
                FullVersionInfo versionInfo = _versionManager.GetFullVersionInfo();

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
