using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Protos;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for communication with the cloud web service via gRPC.
    /// </summary>
    public class CloudCommunicationService
    {
        private readonly ILogger<CloudCommunicationService> _logger;
        private readonly GatewaySettings _settings;
        private readonly LicenseService.LicenseServiceClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudCommunicationService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="settings">Gateway settings.</param>
        public CloudCommunicationService(
            ILogger<CloudCommunicationService> logger,
            IOptions<GatewaySettings> settings)
        {
            _logger = logger;
            _settings = settings.Value;

            HttpClientHandler handler = new HttpClientHandler();
            handler.ClientCertificates.Add(new X509Certificate2(_settings.CertificatePath, _settings.CertificatePassword));

            // Trust the cloud server's certificate (optional if using a trusted CA)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            HttpClient httpClient = new HttpClient(handler);

            GrpcChannel channel = GrpcChannel.ForAddress(_settings.CloudServerAddress, new GrpcChannelOptions
            {
                HttpClient = httpClient
            });

            _client = new LicenseService.LicenseServiceClient(channel);
        }

        /// <summary>
        /// Registers a new device with the cloud web service.
        /// </summary>
        /// <param name="uuid">The device UUID.</param>
        /// <returns>A task representing the registration response.</returns>
        public async Task<DeviceRegistrationResponse> RegisterDeviceAsync(string uuid)
        {
            _logger.LogInformation("Sending device registration request to cloud for UUID: {uuid}", uuid);

            try
            {
                DeviceRegistrationRequest request = new DeviceRegistrationRequest { Uuid = uuid };
                DeviceRegistrationResponse response = await _client.RegisterDeviceAsync(request);

                _logger.LogInformation("Received registration response for UUID: {uuid}", uuid);

                return response;
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC error during device registration for UUID: {uuid}", uuid);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during device registration for UUID: {uuid}", uuid);
                throw;
            }
        }

        /// <summary>
        /// Renews the license for an existing device with the cloud web service.
        /// </summary>
        /// <param name="uuid">The device UUID.</param>
        /// <returns>A task representing the renewal response.</returns>
        public async Task<LicenseRenewalResponse> RenewLicenseAsync(string uuid)
        {
            _logger.LogInformation("Sending license renewal request to cloud for UUID: {uuid}", uuid);

            try
            {
                LicenseRenewalRequest request = new LicenseRenewalRequest { Uuid = uuid };
                LicenseRenewalResponse response = await _client.RenewLicenseAsync(request);

                _logger.LogInformation("Received license renewal response for UUID: {uuid}", uuid);

                return response;
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC error during license renewal for UUID: {uuid}", uuid);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during license renewal for UUID: {uuid}", uuid);
                throw;
            }
        }
    }
}
