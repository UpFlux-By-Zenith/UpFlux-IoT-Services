using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;
using System.Threading.Tasks;

namespace UpFlux.Monitoring.Service
{
    /// <summary>
    /// Sends data to the gateway server via secure TCP using mTLS.
    /// </summary>
    public class TcpClientService
    {
        private readonly string _serverIp;
        private readonly int _serverPort;
        private readonly ILogger<TcpClientService> _logger;
        private readonly ServiceSettings _settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="TcpClientService"/> class.
        /// </summary>
        public TcpClientService(IOptions<ServiceSettings> settings, ILogger<TcpClientService> logger)
        {
            _settings = settings.Value;
            _serverIp = _settings.ServerIp;
            _serverPort = _settings.ServerPort;
            _logger = logger;
        }

        /// <summary>
        /// Sends data to the server over a secure connection using mTLS.
        /// </summary>
        public async Task SendDataAsync(string data)
        {
            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(_settings.ServerIp, _settings.ServerPort);

                using NetworkStream networkStream = client.GetStream();
                
                _logger.LogInformation("Connection established with the Gateway Server.");

                // Send Device UUID to the Gateway Server
                string uuidMessage = $"UUID:{_settings.DeviceUuid}\n";
                byte[] uuidBytes = Encoding.UTF8.GetBytes(uuidMessage);
                await networkStream.WriteAsync(uuidBytes, 0, uuidBytes.Length);
                await networkStream.FlushAsync();

                // Wait for a short time before sending monitoring data so that the server can process the UUID
                await Task.Delay(100);

                // Send monitoring data
                string message = $"MONITORING_DATA:{data}\n";
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                await networkStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                await networkStream.FlushAsync();

                _logger.LogInformation("Monitoring data sent to Gateway Server.");

                // Read acknowledgment
                string response = await ReadMessageAsync(networkStream);
                if (response == "DATA_RECEIVED")
                {
                    _logger.LogInformation("Gateway Server acknowledged data reception.");
                }
                else
                {
                    _logger.LogWarning("Unexpected response from Gateway Server: {response}", response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send monitoring data to Gateway Server.");
            }
        }

        /// <summary>
        /// Helper method to read a message from the SSL stream asynchronously.
        /// </summary>
        /// <param name="sslStream">SSL stream to read from</param>
        /// <returns>Returns the message read from the stream</returns>
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

        /// <summary>
        /// Helper method to read a message from the network stream asynchronously.
        /// </summary>
        private string ReadMessage(NetworkStream networkStream)
        {
            StringBuilder messageData = new StringBuilder();
            byte[] buffer = new byte[2048];
            int bytes = -1;

            do
            {
                bytes = networkStream.Read(buffer, 0, buffer.Length);

                Decoder decoder = Encoding.UTF8.GetDecoder();
                char[] chars = new char[decoder.GetCharCount(buffer, 0, bytes)];
                decoder.GetChars(buffer, 0, bytes, chars, 0);
                messageData.Append(chars);

                // Check for the end of the message.
                if (messageData.ToString().IndexOf("\n") != -1)
                {
                    break;
                }
            } while (bytes != 0);

            return messageData.ToString().Trim();
        }

        /// <summary>
        /// Stores the received license securely.
        /// </summary>
        private void StoreLicense(string license)
        {
            try
            {
                string licensePath = _settings.LicenseFilePath;

                // Ensure the directory exists
                string directory = Path.GetDirectoryName(licensePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write the license to the file securely
                File.WriteAllText(licensePath, license);

                // Set appropriate permissions (e.g., read-only)
                FileInfo fileInfo = new FileInfo(licensePath);
                fileInfo.Attributes = FileAttributes.ReadOnly;

                _logger.LogInformation("License stored securely at {path}", licensePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store license.");
            }
        }

        /// <summary>
        /// Sends a license renewal request to the server.
        /// </summary>
        public void SendLicenseRenewalRequest()
        {
            try
            {
                _logger.LogInformation("Connecting to server {ServerIp}:{ServerPort} for license renewal", _serverIp, _serverPort);

                using (TcpClient client = new TcpClient())
                {
                    client.Connect(_serverIp, _serverPort);

                    using (NetworkStream networkStream = client.GetStream())
                    {
                        _logger.LogInformation("Connection established with the server.");

                        // Send UUID to identify the device
                        string uuidMessage = $"UUID:{_settings.DeviceUuid}\n";
                        byte[] uuidBytes = Encoding.UTF8.GetBytes(uuidMessage);
                        networkStream.Write(uuidBytes, 0, uuidBytes.Length);
                        networkStream.Flush();
                        _logger.LogInformation("Device UUID sent to server: {uuid}", _settings.DeviceUuid);

                        // Send License Renewal Request Command
                        //string command = "RENEW_LICENSE\n";
                        //byte[] commandBytes = Encoding.UTF8.GetBytes(command);
                        //networkStream.Write(commandBytes, 0, commandBytes.Length);
                        //networkStream.Flush();

                        _logger.LogInformation("License renewal request sent to server.");

                        // Receive updated License from Server
                        string message = ReadMessage(networkStream);
                        if (!string.IsNullOrEmpty(message) && message.StartsWith("LICENSE:"))
                        {
                            _logger.LogInformation("Updated license received from server.");

                            // Store license securely
                            string licenseXml = message.Substring("LICENSE:".Length).Trim();
                            StoreLicense(licenseXml);
                        }
                        else
                        {
                            _logger.LogWarning("No license received from server.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during license renewal communication with the server.");
            }
        }
    }
}
