using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.IO;
using Microsoft.Extensions.Logging;
using UpFlux.Update.Service.Models;
using Microsoft.Extensions.Options;

namespace UpFlux.Update.Service.Services
{
    /// <summary>
    /// Watches a directory for new package files.
    /// </summary>
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
        }

        /// <summary>
        /// Starts watching the specified directory.
        /// </summary>
        public void StartWatching(string directory)
        {
            _watcher = new FileSystemWatcher(directory, _config.PackageNamePattern)
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
            };
            _watcher.Created += OnPackageCreated;
        }

        private void OnPackageCreated(object sender, FileSystemEventArgs e)
        {
            _logger.LogInformation($"New package detected: {e.FullPath}");
            UpdatePackage package = new UpdatePackage
            {
                FilePath = e.FullPath,
                Version = ExtractVersionFromFileName(e.Name)
            };
            PackageDetected?.Invoke(this, package);
        }

        /// <summary>
        /// Stops watching the directory.
        /// </summary>
        public void StopWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
        }

        /// <summary>
        /// Extracts the version from the file name.
        /// </summary>
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
