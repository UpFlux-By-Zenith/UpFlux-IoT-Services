using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpFlux.Update.Service.Models;

namespace UpFlux.Update.Service.Services
{
    public class FileWatcherService
    {
        private readonly ILogger<FileWatcherService> _logger;
        private FileSystemWatcher _watcher;
        private readonly Configuration _config;

        public event EventHandler<UpdatePackage> PackageDetected;

        public FileWatcherService(ILogger<FileWatcherService> logger, IOptions<Configuration> configOptions)
        {
            _logger = logger;
            _config = configOptions.Value;

            // Ensure the directories exist
            Directory.CreateDirectory(_config.IncomingPackageDirectory);
            Directory.CreateDirectory(_config.PackageDirectory);
        }

        public void StartWatching()
        {
            _logger.LogInformation("File Watcher started.");
            _watcher = new FileSystemWatcher(_config.IncomingPackageDirectory, _config.PackageNamePattern)
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
            };
            _watcher.Created += OnPackageCreated;
        }

        private void OnPackageCreated(object sender, FileSystemEventArgs e)
        {
            _logger.LogInformation($"New package detected in incoming directory: {e.FullPath}");

            // Wait until the file is accessible
            if (WaitForFile(e.FullPath))
            {
                string destinationPath = Path.Combine(_config.PackageDirectory, Path.GetFileName(e.FullPath));

                // Copy the package to the PackageDirectory
                try
                {
                    File.Copy(e.FullPath, destinationPath, true);
                    _logger.LogInformation($"Package copied to packages directory: {destinationPath}");

                    // Delete the original file from incoming directory
                    File.Delete(e.FullPath);

                    UpdatePackage package = new UpdatePackage
                    {
                        FilePath = destinationPath,
                        Version = ExtractVersionFromFileName(Path.GetFileName(e.FullPath))
                    };
                    PackageDetected?.Invoke(this, package);
                }
                catch (IOException ex)
                {
                    _logger.LogError(ex, $"Failed to copy package to packages directory: {e.FullPath}");
                }
            }
            else
            {
                _logger.LogError($"Failed to access the file: {e.FullPath}");
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
                    Thread.Sleep(delayMilliseconds);
                }
            }
            return false;
        }

        public void StopWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
        }

        private string ExtractVersionFromFileName(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            string pattern = "upflux-monitoring-service_";
            if (name.StartsWith(pattern))
            {
                string versionPart = name.Substring(pattern.Length);
                string version = versionPart.Split('_')[0];
                return version;
            }
            return "0.0.0";
        }
    }
}
