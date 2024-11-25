using System;
using System.IO;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Protos;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// gRPC service implementation for receiving update packages from the cloud.
    /// </summary>
    public class UpdateServiceGrpc : UpdateService.UpdateServiceBase
    {
        private readonly ILogger<UpdateServiceGrpc> _logger;
        private readonly UpdateManagementService _updateManagementService;

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateServiceGrpc"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="updateManagementService">Update management service.</param>
        public UpdateServiceGrpc(
            ILogger<UpdateServiceGrpc> logger,
            UpdateManagementService updateManagementService)
        {
            _logger = logger;
            _updateManagementService = updateManagementService;
        }

        /// <inheritdoc/>
        public override async Task<Google.Protobuf.WellKnownTypes.Empty> SendUpdatePackage(UpdatePackageRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Received update package {packageId} version {version} from cloud.", request.PackageId, request.Version);

            try
            {
                // Save the package data to a temporary file
                string tempFilePath = Path.GetTempFileName();
                await File.WriteAllBytesAsync(tempFilePath, request.PackageData.ToByteArray());

                // Create UpdatePackage model
                UpdatePackage updatePackage = new UpdatePackage
                {
                    PackageId = request.PackageId,
                    Version = request.Version,
                    FileName = request.FileName,
                    FilePath = tempFilePath,
                    Signature = request.Signature.ToByteArray(),
                    TargetDevices = request.TargetDevices.ToArray(),
                    ReceivedAt = DateTime.UtcNow
                };

                // Handle the update package
                await _updateManagementService.HandleUpdatePackageAsync(updatePackage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while receiving update package {packageId}", request.PackageId);
                throw new RpcException(new Status(StatusCode.Internal, "Failed to process update package."));
            }

            return new Google.Protobuf.WellKnownTypes.Empty();
        }
    }
}
