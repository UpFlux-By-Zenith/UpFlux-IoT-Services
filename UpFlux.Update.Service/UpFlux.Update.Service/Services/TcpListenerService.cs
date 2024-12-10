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
        private readonly X509Certificate2 _clientCertificate;
        private readonly X509Certificate2 _trustedCaCertificate;
        private readonly RollbackService _rollbackService;
        private readonly VersionManager _versionManager;

        public TcpListenerService(ILogger<TcpListenerService> logger, IOptions<Configuration> configOptions, RollbackService rollbackService,VersionManager versionManager)
        {
            _logger = logger;
            _config = configOptions.Value;

            // Load client certificate
            _clientCertificate = new X509Certificate2(_config.CertificatePath, _config.CertificatePassword);

            // Load trusted CA certificate
            _trustedCaCertificate = new X509Certificate2(_config.TrustedCaCertificatePath);

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

                using SslStream sslStream = new SslStream(
                    networkStream,
                    false,
                    ValidateServerCertificate,
                    SelectLocalCertificate);

                // Authenticate as client
                await sslStream.AuthenticateAsClientAsync(
                    _config.GatewayServerIp,
                    new X509CertificateCollection { _clientCertificate },
                    System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);

                _logger.LogInformation("Secure connection established with the Gateway Server.");

                // Send Device UUID to the Gateway Server
                string uuidMessage = $"{_config.DeviceUuid}\n";
                byte[] uuidBytes = Encoding.UTF8.GetBytes(uuidMessage);
                await sslStream.WriteAsync(uuidBytes, 0, uuidBytes.Length);
                await sslStream.FlushAsync();

                _logger.LogInformation("Device UUID sent to Gateway Server: {uuid}", _config.DeviceUuid);

                // Receive commands from the Gateway Server
                await ReceiveCommandsAsync(sslStream);
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

        private async Task ReceiveCommandsAsync(SslStream sslStream)
        {
            try
            {
                while (true)
                {
                    string command = await ReadMessageAsync(sslStream);
                    if (string.IsNullOrEmpty(command))
                    {
                        _logger.LogInformation("No command received. Closing connection.");
                        break;
                    }

                    _logger.LogInformation("Received command: {command}", command);

                    if (command.StartsWith("SEND_PACKAGE"))
                    {
                        // Handle receiving the package
                        await ReceivePackageAsync(sslStream, command);
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
                        await SendLogsAsync(sslStream);
                    }
                    else if (command.StartsWith("ROLLBACK:"))
                    {
                        // Handle rollback command
                        string version = command.Substring("ROLLBACK:".Length).Trim();
                        await HandleRollbackCommandAsync(sslStream, version);
                    }
                    else if (command == "GET_VERSIONS")
                    {
                        // Handle GET_VERSIONS command
                        await HandleGetVersionsCommandAsync(sslStream);
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

        private async Task ReceivePackageAsync(SslStream sslStream, string command)
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
                await sslStream.WriteAsync(ackBytes, 0, ackBytes.Length);
                await sslStream.FlushAsync();

                // Read the length of the package (as 4-byte integer)
                byte[] lengthBytes = new byte[4];
                int totalBytesRead = 0;
                while (totalBytesRead < 4)
                {
                    int bytesRead = await sslStream.ReadAsync(lengthBytes, totalBytesRead, 4 - totalBytesRead);
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
                    int bytesRead = await sslStream.ReadAsync(packageData, totalBytesRead, packageLength - totalBytesRead);
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
        private async Task<string> ReadMessageAsync(SslStream sslStream)
        {
            StringBuilder messageData = new StringBuilder();
            byte[] buffer = new byte[1024];
            int bytesRead = -1;

            do
            {
                bytesRead = await sslStream.ReadAsync(buffer, 0, buffer.Length);
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

        private bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // Validate server certificate against trusted CA
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            _logger.LogWarning("Server certificate validation failed: {errors}", sslPolicyErrors);
            return false;
        }

        private X509Certificate SelectLocalCertificate(
            object sender,
            string targetHost,
            X509CertificateCollection localCertificates,
            X509Certificate remoteCertificate,
            string[] acceptableIssuers)
        {
            return _clientCertificate;
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
        private async Task SendLogsAsync(SslStream sslStream)
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
                await sslStream.WriteAsync(fileCountBytes, 0, fileCountBytes.Length);
                await sslStream.FlushAsync();

                foreach (var log in logsToSend)
                {
                    // Send the file name length and file name
                    byte[] fileNameBytes = Encoding.UTF8.GetBytes(log.FileName);
                    byte[] fileNameLengthBytes = BitConverter.GetBytes(fileNameBytes.Length);
                    await sslStream.WriteAsync(fileNameLengthBytes, 0, fileNameLengthBytes.Length);
                    await sslStream.WriteAsync(fileNameBytes, 0, fileNameBytes.Length);
                    await sslStream.FlushAsync();

                    // Read the file data
                    byte[] logBytes = File.ReadAllBytes(log.FilePath);
                    int logLength = logBytes.Length;

                    // Send the length of the log file (as 4-byte integer)
                    byte[] lengthBytes = BitConverter.GetBytes(logLength);
                    await sslStream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await sslStream.FlushAsync();

                    // Send the log file data
                    await sslStream.WriteAsync(logBytes, 0, logBytes.Length);
                    await sslStream.FlushAsync();
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
                using SslStream sslStream = new SslStream(
                    networkStream,
                    false,
                    ValidateServerCertificate,
                    SelectLocalCertificate);

                // Authenticate as client
                await sslStream.AuthenticateAsClientAsync(
                    _config.GatewayServerIp,
                    new X509CertificateCollection { _clientCertificate },
                    System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);

                _logger.LogInformation("Secure connection established with the Gateway Server for notification.");

                // Send Device UUID to the Gateway Server
                string uuidMessage = $"{_config.DeviceUuid}\n";
                byte[] uuidBytes = Encoding.UTF8.GetBytes(uuidMessage);
                await sslStream.WriteAsync(uuidBytes, 0, uuidBytes.Length);
                await sslStream.FlushAsync();

                // Send the notification message
                string notificationMessage = $"NOTIFICATION:{message}\n";
                byte[] messageBytes = Encoding.UTF8.GetBytes(notificationMessage);
                await sslStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                await sslStream.FlushAsync();

                _logger.LogInformation("Notification sent to Gateway Server: {message}", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notification to Gateway Server.");
            }
        }

        private async Task HandleRollbackCommandAsync(SslStream sslStream, string version)
        {
            try
            {
                _logger.LogInformation("Initiating rollback to version {version}", version);

                // Send acknowledgment
                string ackMessage = "ROLLBACK_INITIATED\n";
                byte[] ackBytes = Encoding.UTF8.GetBytes(ackMessage);
                await sslStream.WriteAsync(ackBytes, 0, ackBytes.Length);
                await sslStream.FlushAsync();

                // Perform rollback
                bool rollbackResult = await _rollbackService.ManualRollbackAsync(version);

                if (rollbackResult)
                {
                    _logger.LogInformation("Rollback to version {version} completed successfully.", version);

                    // Send confirmation
                    string confirmationMessage = "ROLLBACK_COMPLETED\n";
                    byte[] confirmationBytes = Encoding.UTF8.GetBytes(confirmationMessage);
                    await sslStream.WriteAsync(confirmationBytes, 0, confirmationBytes.Length);
                    await sslStream.FlushAsync();

                    // Send a notification back to the Gateway Server
                    await SendNotificationAsync($"Rollback to version {version} completed successfully.");
                }
                else
                {
                    _logger.LogError("Rollback to version {version} failed.", version);

                    // Send failure message
                    string failureMessage = "ROLLBACK_FAILED\n";
                    byte[] failureBytes = Encoding.UTF8.GetBytes(failureMessage);
                    await sslStream.WriteAsync(failureBytes, 0, failureBytes.Length);
                    await sslStream.FlushAsync();

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
                await sslStream.WriteAsync(failureBytes, 0, failureBytes.Length);
                await sslStream.FlushAsync();
            }
        }

        private async Task HandleGetVersionsCommandAsync(SslStream sslStream)
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
                await sslStream.WriteAsync(versionInfoBytes, 0, versionInfoBytes.Length);
                await sslStream.FlushAsync();

                _logger.LogInformation("Version information sent to Gateway Server.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling GET_VERSIONS command.");
            }
        }

    }
}
