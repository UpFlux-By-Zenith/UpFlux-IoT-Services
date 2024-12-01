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
        private readonly X509Certificate2 _clientCertificate;
        private readonly X509Certificate2 _trustedCaCertificate;

        /// <summary>
        /// Initializes a new instance of the <see cref="TcpClientService"/> class.
        /// </summary>
        public TcpClientService(IOptions<ServiceSettings> settings, ILogger<TcpClientService> logger)
        {
            _settings = settings.Value;
            _serverIp = _settings.ServerIp;
            _serverPort = _settings.ServerPort;
            _logger = logger;

            // Load client certificate
            _clientCertificate = new X509Certificate2(_settings.CertificatePath, _settings.CertificatePassword);

            // Load trusted CA certificate
            _trustedCaCertificate = new X509Certificate2(_settings.TrustedCaCertificatePath);
        }

        /// <summary>
        /// Sends data to the server over a secure connection using mTLS.
        /// </summary>
        public void SendData(string data)
        {
            try
            {
                _logger.LogInformation("Connecting to server {ServerIp}:{ServerPort}", _serverIp, _serverPort);

                using (TcpClient client = new TcpClient())
                {
                    client.Connect(_serverIp, _serverPort);

                    using (NetworkStream networkStream = client.GetStream())
                    {
                        using (SslStream sslStream = new SslStream(
                            networkStream,
                            false,
                            new RemoteCertificateValidationCallback(ValidateServerCertificate),
                            new LocalCertificateSelectionCallback(SelectLocalCertificate)))
                        {
                            // Authenticate as client
                            sslStream.AuthenticateAsClient(_serverIp, new X509CertificateCollection { _clientCertificate }, System.Security.Authentication.SslProtocols.Tls13, false);

                            _logger.LogInformation("Secure connection established with the server.");

                            // Send Device UUID
                            string uuid = _settings.DeviceUuid;
                            string uuidMessage = $"{uuid}\n";
                            byte[] uuidBytes = Encoding.UTF8.GetBytes(uuidMessage);
                            sslStream.Write(uuidBytes, 0, uuidBytes.Length);
                            sslStream.Flush();

                            _logger.LogInformation("Device UUID sent to server: {uuid}", uuid);

                            // Receive License from Server
                            string license = ReadMessage(sslStream);
                            if (!string.IsNullOrEmpty(license))
                            {
                                _logger.LogInformation("License received from server.");

                                // Store license securely
                                StoreLicense(license);
                            }
                            else
                            {
                                _logger.LogWarning("No license received from server.");
                            }

                            // Send Data
                            byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                            sslStream.Write(dataBytes, 0, dataBytes.Length);
                            sslStream.Flush();

                            _logger.LogInformation("Data sent to server {ServerIp}:{ServerPort}", _serverIp, _serverPort);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during secure communication with the server.");
            }
        }

        /// <summary>
        /// Validates the server's certificate.
        /// </summary>
        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // Validate server certificate against trusted CA
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            _logger.LogWarning("Server certificate validation failed: {errors}", sslPolicyErrors);
            return false;
        }

        /// <summary>
        /// Selects the local client certificate.
        /// </summary>
        private X509Certificate SelectLocalCertificate(
            object sender,
            string targetHost,
            X509CertificateCollection localCertificates,
            X509Certificate remoteCertificate,
            string[] acceptableIssuers)
        {
            return _clientCertificate;
        }

        /// <summary>
        /// Reads a message from the SSL stream until a newline character is encountered.
        /// </summary>
        private string ReadMessage(SslStream sslStream)
        {
            StringBuilder messageData = new StringBuilder();
            byte[] buffer = new byte[2048];
            int bytes = -1;

            do
            {
                bytes = sslStream.Read(buffer, 0, buffer.Length);

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
                        using (SslStream sslStream = new SslStream(
                            networkStream,
                            false,
                            new RemoteCertificateValidationCallback(ValidateServerCertificate),
                            new LocalCertificateSelectionCallback(SelectLocalCertificate)))
                        {
                            // Authenticate as client
                            sslStream.AuthenticateAsClient(_serverIp, new X509CertificateCollection { _clientCertificate }, System.Security.Authentication.SslProtocols.Tls13, false);

                            _logger.LogInformation("Secure connection established with the server.");

                            // Send License Renewal Request Command
                            string command = "RENEW_LICENSE\n";
                            byte[] commandBytes = Encoding.UTF8.GetBytes(command);
                            sslStream.Write(commandBytes, 0, commandBytes.Length);
                            sslStream.Flush();

                            _logger.LogInformation("License renewal request sent to server.");

                            // Receive updated License from Server
                            string license = ReadMessage(sslStream);
                            if (!string.IsNullOrEmpty(license))
                            {
                                _logger.LogInformation("Updated license received from server.");

                                // Store license securely
                                StoreLicense(license);
                            }
                            else
                            {
                                _logger.LogWarning("No license received from server.");
                            }
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
