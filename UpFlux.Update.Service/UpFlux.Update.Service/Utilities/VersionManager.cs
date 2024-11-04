using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UpFlux.Update.Service.Models;
using Microsoft.Extensions.Options;

namespace UpFlux.Update.Service.Utilities
{
    /// <summary>
    /// Manages stored versions of the UpFlux Monitoring Service packages.
    /// </summary>
    public class VersionManager
    {
        private readonly Configuration _config;

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionManager"/> class with configuration settings.
        /// </summary>
        public VersionManager(IOptions<Configuration> config)
        {
            _config = config.Value;
            Directory.CreateDirectory(_config.PackageDirectory);
        }

        /// <summary>
        /// Stores the package and manages version limits.
        /// </summary>
        public void StorePackage(UpdatePackage package)
        {
            string destinationPath = Path.Combine(_config.PackageDirectory, Path.GetFileName(package.FilePath));

            const int maxAttempts = 5;
            int attempt = 0;
            bool copySuccess = false;

            while (!copySuccess && attempt < maxAttempts)
            {
                try
                {
                    File.Copy(package.FilePath, destinationPath, true);
                    copySuccess = true;
                }
                catch (IOException ex)
                {
                    attempt++;
                    Thread.Sleep(500); // Wait before retrying
                    if (attempt == maxAttempts)
                    {
                        throw new IOException($"Failed to copy package after {maxAttempts} attempts.", ex);
                    }
                }
            }

            CleanOldVersions();
        }

        /// <summary>
        /// Retrieves stored packages.
        /// </summary>
        public List<UpdatePackage> GetStoredPackages()
        {
            string[] files = Directory.GetFiles(_config.PackageDirectory, _config.PackageNamePattern);
            return files.Select(f => new UpdatePackage
            {
                FilePath = f,
                Version = GetVersionFromFileName(f)
            })
            .OrderByDescending(p => Version.Parse(p.Version))
            .ToList();
        }

        /// <summary>
        /// Cleans old versions beyond the maximum stored versions.
        /// </summary>
        private void CleanOldVersions()
        {
            List<UpdatePackage> packages = GetStoredPackages();
            if (packages.Count > _config.MaxStoredVersions)
            {
                IEnumerable<UpdatePackage> packagesToRemove = packages.Skip(_config.MaxStoredVersions);
                foreach (UpdatePackage pkg in packagesToRemove)
                {
                    File.Delete(pkg.FilePath);
                }
            }
        }

        /// <summary>
        /// Extracts the version from the package file name.
        /// </summary>
        private string GetVersionFromFileName(string fileName)
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

        /// <summary>
        /// Gets the previous version package excluding the current version.
        /// </summary>
        public UpdatePackage GetPreviousVersion(string currentVersion)
        {
            List<UpdatePackage> packages = GetStoredPackages();
            return packages.FirstOrDefault(p => p.Version != currentVersion);
        }
    }
}
