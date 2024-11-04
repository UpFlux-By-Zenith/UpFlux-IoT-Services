using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpFlux.Update.Service.Models;

namespace UpFlux.Update.Service.Services
{
    public class TcpListenerService
    {
        private readonly ILogger<TcpListenerService> _logger;
        private readonly Configuration _config;
        private TcpListener _tcpListener;
        private bool _isListening;

        public TcpListenerService(ILogger<TcpListenerService> logger, IOptions<Configuration> configOptions)
        {
            _logger = logger;
            _config = configOptions.Value;

            // Ensure the incoming directory exists
            Directory.CreateDirectory(_config.IncomingPackageDirectory);
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

                // Read the length of the filename (4 bytes)
                byte[] fileNameLengthBytes = new byte[4];
                int bytesRead = await ReadExactAsync(networkStream, fileNameLengthBytes, 0, 4);
                if (bytesRead < 4)
                {
                    _logger.LogError("Failed to read the length of the filename.");
                    return;
                }
                int fileNameLength = BitConverter.ToInt32(fileNameLengthBytes, 0);

                // Read the filename
                byte[] fileNameBytes = new byte[fileNameLength];
                bytesRead = await ReadExactAsync(networkStream, fileNameBytes, 0, fileNameLength);
                if (bytesRead < fileNameLength)
                {
                    _logger.LogError("Failed to read the filename.");
                    return;
                }
                string fileName = Encoding.UTF8.GetString(fileNameBytes);

                // Validate the filename to prevent security issues
                if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    _logger.LogError("Invalid filename received.");
                    return;
                }

                // Generate the destination path in the IncomingPackageDirectory
                string destinationPath = Path.Combine(_config.IncomingPackageDirectory, fileName);

                // Read the file contents and save to destinationPath
                using FileStream fileStream = File.Create(destinationPath);
                await networkStream.CopyToAsync(fileStream);
                await fileStream.FlushAsync();

                _logger.LogInformation($"Received package and saved to {destinationPath}");

                // The FileWatcherService will detect the new file and handle it
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

        // Helper method to read an exact number of bytes from the stream
        private async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset + totalBytesRead, count - totalBytesRead);
                if (bytesRead == 0)
                {
                    // End of stream reached before reading the required number of bytes
                    break;
                }
                totalBytesRead += bytesRead;
            }
            return totalBytesRead;
        }

        public void StopListening()
        {
            _isListening = false;
            _tcpListener.Stop();
        }
    }
}
