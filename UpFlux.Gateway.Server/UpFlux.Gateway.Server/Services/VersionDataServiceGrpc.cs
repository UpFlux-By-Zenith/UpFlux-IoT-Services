using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Protos;
using System.Linq;
using Google.Protobuf.WellKnownTypes;
using System.Collections.Generic;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// gRPC service for handling version retrieval requests from the cloud.
    /// </summary>
    public class VersionDataServiceGrpc : VersionDataService.VersionDataServiceBase
    {
        private readonly ILogger<VersionDataServiceGrpc> _logger;
        private readonly VersionControlService _versionControlService;

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionDataServiceGrpc"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="versionControlService">Version control service.</param>
        public VersionDataServiceGrpc(
            ILogger<VersionDataServiceGrpc> logger,
            VersionControlService versionControlService)
        {
            _logger = logger;
            _versionControlService = versionControlService;
        }

        /// <inheritdoc/>
        public override async Task<VersionDataResponse> RequestVersionData(VersionDataRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Received version data request from cloud.");

            try
            {
                // Retrieve versions from devices
                await _versionControlService.RetrieveVersionsAsync();

                // Get all version info
                List<Models.VersionInfo> allVersionInfo = _versionControlService.GetAllVersionInfo();

                // Group versions by device UUID
                Dictionary<string, List<Models.VersionInfo>> deviceVersionsDict = allVersionInfo.GroupBy(v => v.DeviceUUID)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Construct the response
                VersionDataResponse response = new VersionDataResponse
                {
                    Success = true,
                    Message = "Version data retrieved successfully."
                };

                foreach (KeyValuePair<string, List<Models.VersionInfo>> deviceEntry in deviceVersionsDict)
                {
                    DeviceVersions deviceVersions = new DeviceVersions
                    {
                        DeviceUuid = deviceEntry.Key,
                        Versions =
                        {
                            deviceEntry.Value.Select(v => new Protos.VersionInfo
                            {
                                Version = v.Version,
                                InstalledAt = Timestamp.FromDateTime(v.InstalledAt)
                            })
                        }
                    };

                    response.DeviceVersionsList.Add(deviceVersions);
                }

                _logger.LogInformation("Sending version data response to cloud.");

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during version retrieval.");
                return new VersionDataResponse
                {
                    Success = false,
                    Message = "An error occurred during version retrieval."
                };
            }
        }
    }
}
