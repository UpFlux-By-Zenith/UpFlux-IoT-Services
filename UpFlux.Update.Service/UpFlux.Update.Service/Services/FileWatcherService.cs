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
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
            };
            _watcher.Created += OnPackageCreatedOrRenamed;
            _watcher.Renamed += OnPackageCreatedOrRenamed;

            _watcher.EnableRaisingEvents = true;
        }

        private void OnPackageCreatedOrRenamed(object sender, FileSystemEventArgs e)
        {
            _logger.LogInformation($"New package detected in incoming directory: {e.FullPath}");

            try
            {
                // Copy into packages dir
                string destinationPath = Path.Combine(_config.PackageDirectory, Path.GetFileName(e.FullPath));

                File.Copy(e.FullPath, destinationPath, overwrite: true);
                _logger.LogInformation("Package copied to packages directory: {dest}", destinationPath);

                // remove from incoming
                File.Delete(e.FullPath);

                UpdatePackage package = new UpdatePackage
                {
                    FilePath = destinationPath,
                    Version = ExtractVersionFromFileName(Path.GetFileName(e.FullPath))
                };

                // Raise event
                PackageDetected?.Invoke(this, package);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to copy package to packages directory: {path}", e.FullPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in OnPackageCreatedOrRenamed.");
            }
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
