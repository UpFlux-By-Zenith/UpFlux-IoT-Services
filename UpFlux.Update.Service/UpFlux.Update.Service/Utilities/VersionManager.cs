using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;
using UpFlux.Update.Service.Models;

namespace UpFlux.Update.Service.Utilities
{
    public class VersionManager
    {
        private readonly Configuration _config;
        private readonly ILogger<VersionManager> _logger;

        public VersionManager(IOptions<Configuration> config, ILogger<VersionManager> logger)
        {
            _config = config.Value;

            // Ensure the package directory exists
            Directory.CreateDirectory(_config.PackageDirectory);
            _logger = logger;
        }

        public void StorePackage(UpdatePackage package)
        {
            CleanOldVersions();
        }

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

        public UpdatePackage GetPreviousVersion(string currentVersion)
        {
            List<UpdatePackage> packages = GetStoredPackages();
            return packages.FirstOrDefault(p => p.Version != currentVersion);
        }

        public UpdatePackage GetPackageByVersion(string version)
        {
            List<UpdatePackage> packages = GetStoredPackages();
            return packages.FirstOrDefault(p => p.Version == version);
        }

        private string GetCurrentInstalledVersion()
        {
            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = "dpkg",
                    Arguments = "-s upflux-monitoring-service",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                Process process = new Process { StartInfo = processStartInfo };
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Parse the output to get the version
                string versionLine = output.Split('\n').FirstOrDefault(line => line.StartsWith("Version:"));
                if (!string.IsNullOrEmpty(versionLine))
                {
                    string version = versionLine.Split(':').Last().Trim();
                    return version;
                }
                else
                {
                    _logger.LogWarning("Could not find the version information in dpkg output.");
                    return "Unknown";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving current installed version.");
                return "Unknown";
            }
        }

        /// <summary>
        /// Retrieves the current installed version and all available versions from the package directory.
        /// </summary>
        /// <returns>An object containing the current version and a list of available versions.</returns>
        public object GetVersionInfo()
        {
            string currentVersion = GetCurrentInstalledVersion();
            List<UpdatePackage> packages = GetStoredPackages();
            List<string> availableVersions = packages.Select(p => p.Version).ToList();

            return new
            {
                CurrentVersion = currentVersion,
                AvailableVersions = availableVersions
            };
        }

        public FullVersionInfo GetFullVersionInfo()
        {
            // Get the current installed version
            string currentVersion = GetCurrentInstalledVersion();

            // Get the installation date of the current version from the package file using the GetPackageByVersion method
            UpdatePackage currentPackage = GetPackageByVersion(currentVersion);
            DateTime currentVersionInstalledAt = File.GetCreationTimeUtc(currentPackage.FilePath);

            VersionRecord currentRecord = new VersionRecord
            {
                Version = currentVersion,
                InstalledAt = currentVersionInstalledAt
            };

            // Gather other packages in /opt/upflux-update-service/packages
            List<VersionRecord> availableList = new List<VersionRecord>();
            string[] debFiles = Directory.GetFiles(_config.PackageDirectory, _config.PackageNamePattern);
            foreach (string debPath in debFiles)
            {
                string version = GetVersionFromFileName(debPath);
                DateTime fileTime = File.GetCreationTimeUtc(debPath);

                availableList.Add(new VersionRecord
                {
                    Version = version,
                    InstalledAt = fileTime
                });
            }

            // remove the "Current" from the "Available" if it's in that folder
            availableList.RemoveAll(r => r.Version == currentVersion);

            return new FullVersionInfo
            {
                Current = currentRecord,
                Available = availableList
            };
        }
    }
}
