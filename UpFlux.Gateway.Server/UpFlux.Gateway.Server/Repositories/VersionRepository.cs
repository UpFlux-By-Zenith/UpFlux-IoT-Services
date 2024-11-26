using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Dapper;
using Microsoft.Extensions.Logging;
using UpFlux.Gateway.Server.Models;

namespace UpFlux.Gateway.Server.Repositories
{
    /// <summary>
    /// Repository for managing version information in the database.
    /// </summary>
    public class VersionRepository
    {
        private readonly ILogger<VersionRepository> _logger;
        private readonly string _connectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionRepository"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="settings">Gateway settings.</param>
        public VersionRepository(ILogger<VersionRepository> logger, GatewaySettings settings)
        {
            _logger = logger;
            _connectionString = settings.DatabaseConnectionString;

            InitializeDatabase();
        }

        /// <summary>
        /// Initializes the database and creates the necessary tables.
        /// </summary>
        private void InitializeDatabase()
        {
            using SQLiteConnection connection = new SQLiteConnection(_connectionString);
            connection.Execute(
                @"CREATE TABLE IF NOT EXISTS VersionInfo (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DeviceUUID TEXT NOT NULL,
                    Version TEXT NOT NULL,
                    InstalledAt DATETIME NOT NULL
                )");

            connection.Execute(
                @"CREATE INDEX IF NOT EXISTS idx_device_uuid ON VersionInfo (DeviceUUID)");
        }

        /// <summary>
        /// Adds a new version information entry for a device.
        /// </summary>
        /// <param name="versionInfo">The version information to add.</param>
        public void AddVersionInfo(VersionInfo versionInfo)
        {
            using SQLiteConnection connection = new SQLiteConnection(_connectionString);
            // Check if the version already exists for the device
            VersionInfo? existingVersion = connection.QueryFirstOrDefault<VersionInfo>(
                @"SELECT * FROM VersionInfo WHERE DeviceUUID = @DeviceUUID AND Version = @Version",
                new { versionInfo.DeviceUUID, versionInfo.Version });

            if (existingVersion == null)
            {
                connection.Execute(
                    @"INSERT INTO VersionInfo (DeviceUUID, Version, InstalledAt)
              VALUES (@DeviceUUID, @Version, @InstalledAt)",
                    versionInfo);
            }
            else
            {
                _logger.LogInformation("Version {version} for device {uuid} already exists in the database.", versionInfo.Version, versionInfo.DeviceUUID);
            }
        }


        /// <summary>
        /// Retrieves all version information from the database.
        /// </summary>
        /// <returns>A list of VersionInfo objects.</returns>
        public List<VersionInfo> GetAllVersionInfo()
        {
            using SQLiteConnection connection = new SQLiteConnection(_connectionString);
            return connection.Query<VersionInfo>("SELECT * FROM VersionInfo").AsList();
        }

        /// <summary>
        /// Retrieves all versions for a specific device.
        /// </summary>
        /// <param name="deviceUuid">The UUID of the device.</param>
        /// <returns>A list of VersionInfo objects for the device.</returns>
        public List<VersionInfo> GetVersionsByDevice(string deviceUuid)
        {
            using SQLiteConnection connection = new SQLiteConnection(_connectionString);
            return connection.Query<VersionInfo>(
                "SELECT * FROM VersionInfo WHERE DeviceUUID = @DeviceUUID",
                new { DeviceUUID = deviceUuid }).AsList();
        }
    }
}
