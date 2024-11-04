using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;
using UpFlux.Update.Service.Models;

namespace UpFlux.Update.Service.Utilities
{
    public class VersionManager
    {
        private readonly Configuration _config;

        public VersionManager(IOptions<Configuration> config)
        {
            _config = config.Value;

            // Ensure the package directory exists
            Directory.CreateDirectory(_config.PackageDirectory);
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
    }
}
