using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using UpFlux.Gateway.Server.Models;

namespace UpFlux.Gateway.Server.Repositories
{
    /// <summary>
    /// Repository for accessing and manipulating device data in the SQLite database.
    /// </summary>
    public class DeviceRepository
    {
        private readonly string _connectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceRepository"/> class.
        /// </summary>
        /// <param name="settings">Gateway settings containing the database connection string.</param>
        public DeviceRepository(IOptions<GatewaySettings> settings)
        {
            _connectionString = settings.Value.DatabaseConnectionString;
            InitializeDatabase();
        }

        /// <summary>
        /// Initializes the devices table in the database.
        /// </summary>
        private void InitializeDatabase()
        {
            using SqliteConnection connection = new SqliteConnection(_connectionString);
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Devices (
                    UUID TEXT PRIMARY KEY,
                    IPAddress TEXT,
                    License TEXT,
                    LicenseExpiration TEXT,
                    LastSeen TEXT,
                    RegistrationStatus TEXT
                )
            ");
        }

        /// <summary>
        /// Adds a new device or updates an existing device in the database.
        /// </summary>
        /// <param name="device">The device to add or update.</param>
        public void AddOrUpdateDevice(Device device)
        {
            using SqliteConnection connection = new SqliteConnection(_connectionString);
            var existingDevice = GetDeviceByUuid(device.UUID);

            if (existingDevice == null)
            {
                // Insert new device
                connection.Execute(@"
                    INSERT INTO Devices (UUID, IPAddress, License, LicenseExpiration, LastSeen, RegistrationStatus)
                    VALUES (@UUID, @IPAddress, @License, @LicenseExpiration, @LastSeen, @RegistrationStatus)
                ", device);
            }
            else
            {
                // Update existing device
                connection.Execute(@"
                    UPDATE Devices
                    SET IPAddress = @IPAddress,
                        License = @License,
                        LicenseExpiration = @LicenseExpiration,
                        LastSeen = @LastSeen,
                        RegistrationStatus = @RegistrationStatus
                    WHERE UUID = @UUID
                ", device);
            }
        }

        /// <summary>
        /// Retrieves a device by its UUID.
        /// </summary>
        /// <param name="uuid">The UUID of the device.</param>
        /// <returns>The device if found; otherwise, null.</returns>
        public Device GetDeviceByUuid(string uuid)
        {
            using SqliteConnection connection = new SqliteConnection(_connectionString);
            return connection.QueryFirstOrDefault<Device>(@"
                SELECT * FROM Devices WHERE UUID = @UUID
            ", new { UUID = uuid });
        }

        /// <summary>
        /// Retrieves all devices from the database.
        /// </summary>
        /// <returns>A list of all devices.</returns>
        public List<Device> GetAllDevices()
        {
            using SqliteConnection connection = new SqliteConnection(_connectionString);
            return connection.Query<Device>("SELECT * FROM Devices").ToList();
        }

        /// <summary>
        /// Deletes a device from the database by its UUID.
        /// </summary>
        /// <param name="uuid">The UUID of the device to delete.</param>
        public void DeleteDevice(string uuid)
        {
            using SqliteConnection connection = new SqliteConnection(_connectionString);
            connection.Execute("DELETE FROM Devices WHERE UUID = @UUID", new { UUID = uuid });
        }
    }
}
