using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using UpFlux.Update.Service.Models;

namespace UpFlux.Update.Service.Services
{
    /// <summary>
    /// Listens for incoming packages via TCP. from the gateway server.
    /// </summary>
    public class TcpListenerService
    {
        private readonly ILogger<TcpListenerService> _logger;
        private TcpListener _tcpListener;
        private bool _isListening;

        public event EventHandler<UpdatePackage> PackageReceived;

        public TcpListenerService(ILogger<TcpListenerService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Starts listening for incoming TCP connections.
        /// </summary>
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
                string tempFilePath = Path.GetTempFileName();

                using FileStream fileStream = File.Create(tempFilePath);
                await networkStream.CopyToAsync(fileStream);

                _logger.LogInformation($"Received package and saved to {tempFilePath}");

                UpdatePackage package = new UpdatePackage
                {
                    FilePath = tempFilePath,
                    Version = ExtractVersionFromPackage(tempFilePath)
                };

                PackageReceived?.Invoke(this, package);
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

        /// <summary>
        /// Stops listening for TCP connections.
        /// </summary>
        public void StopListening()
        {
            _isListening = false;
            _tcpListener.Stop();
        }

        /// <summary>
        /// Extracts the version from the package file.
        /// </summary>
        private string ExtractVersionFromPackage(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string pattern = "upflux-monitoring-service_";
            if (fileName.StartsWith(pattern))
            {
                string versionPart = fileName.Substring(pattern.Length);
                string version = versionPart.Split('_')[0];
                return version;
            }
            return "0.0.0";
        }
    }
}

