using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Protos;
using System.Linq;
using Google.Protobuf.WellKnownTypes;
using System.Collections.Generic;
using UpFlux.Gateway.Server.Repositories;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// gRPC service for handling version retrieval requests from the cloud.
    /// </summary>
    public class VersionDataServiceGrpc : VersionDataService.VersionDataServiceBase
    {
        private readonly ILogger<VersionDataServiceGrpc> _logger;
        private readonly DeviceRepository _deviceRepository;
        private readonly DeviceCommunicationService _deviceCommunicationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionDataServiceGrpc"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="deviceRepository">Device repository.</param>
        /// <param name="deviceCommunicationService">Device communication service.</param>
        public VersionDataServiceGrpc(
            ILogger<VersionDataServiceGrpc> logger,
            DeviceRepository deviceRepository,
            DeviceCommunicationService deviceCommunicationService)
        {
            _logger = logger;
            _deviceRepository = deviceRepository;
            _deviceCommunicationService = deviceCommunicationService;
        }

        /// <inheritdoc/>
        public override async Task<VersionDataResponse> RequestVersionData(VersionDataRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Received version data request from cloud.");

            VersionDataResponse response = new VersionDataResponse
            {
                Success = true,
                Message = "Version data retrieved successfully."
            };

            List<Device> devices = _deviceRepository.GetAllDevices();

            foreach (Device dev in devices)
            {
                if(dev.UUID == null)
                {
                    continue;
                }

                // Retrieve the FullVersionInfo object from the device
                FullVersionInfo info = await _deviceCommunicationService.RequestVersionInfoAsync(dev);
                if (info == null)
                {
                    continue;
                }

                // Build the proto DeviceVersions
                DeviceVersions protoDevVers = new DeviceVersions
                {
                    DeviceUuid = dev.UUID
                };

                if (info.Current != null)
                {
                    protoDevVers.Current = new Protos.VersionInfo
                    {
                        Version = info.Current.Version,
                        InstalledAt = Timestamp.FromDateTime(info.Current.InstalledAt.ToUniversalTime())
                    };
                }

                foreach (VersionRecord rec in info.Available)
                {
                    protoDevVers.Available.Add(new Protos.VersionInfo
                    {
                        Version = rec.Version,
                        InstalledAt = Timestamp.FromDateTime(rec.InstalledAt.ToUniversalTime())
                    });
                }

                response.DeviceVersionsList.Add(protoDevVers);
            }
            return response;
        }
    }
}
