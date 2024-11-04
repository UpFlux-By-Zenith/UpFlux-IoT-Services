using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UpFlux.Update.Service.Models;

namespace UpFlux.Update.Service.Services
{
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
                await fileStream.FlushAsync();
                fileStream.Close();

                _logger.LogInformation($"Received package and saved to {tempFilePath}");

                // Wait until the file is accessible
                if (WaitForFile(tempFilePath))
                {
                    UpdatePackage package = new UpdatePackage
                    {
                        FilePath = tempFilePath,
                        Version = ExtractVersionFromPackage(tempFilePath)
                    };

                    PackageReceived?.Invoke(this, package);
                }
                else
                {
                    _logger.LogError($"Failed to access the file: {tempFilePath}");
                }
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

        private bool WaitForFile(string filePath)
        {
            const int maxAttempts = 10;
            const int delayMilliseconds = 500;

            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        if (stream.Length > 0)
                        {
                            return true;
                        }
                    }
                }
                catch (IOException)
                {
                    // The file is still locked, wait and retry
                    Thread.Sleep(delayMilliseconds);
                }
            }
            return false;
        }

        public void StopListening()
        {
            _isListening = false;
            _tcpListener.Stop();
        }

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
