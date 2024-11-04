using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UpFlux.Monitoring.Service
{
    /// <summary>
    /// Sends data to the gateway server via TCP.
    /// </summary>
    public class TcpClientService
    {
        private readonly string _serverIp;
        private readonly int _serverPort;
        private readonly ILogger<TcpClientService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TcpClientService"/> class.
        /// </summary>
        public TcpClientService(IOptions<ServiceSettings> settings, ILogger<TcpClientService> logger)
        {
            _serverIp = settings.Value.ServerIp;
            _serverPort = settings.Value.ServerPort;
            _logger = logger;
        }

        /// <summary>
        /// Sends data to the server.
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
                        byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                        networkStream.Write(dataBytes, 0, dataBytes.Length);
                        networkStream.Flush();
                    }
                }

                _logger.LogInformation("Data sent to server {ServerIp}:{ServerPort}", _serverIp, _serverPort);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending data to server.");
            }
        }
    }
}
