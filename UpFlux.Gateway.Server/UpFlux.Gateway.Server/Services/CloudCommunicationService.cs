using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
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
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly MonitoringService.MonitoringServiceClient _monitoringClient;
        private readonly CloudLogService.CloudLogServiceClient _cloudLogClient;
        private readonly AlertService.AlertServiceClient _alertClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudCommunicationService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="settings">Gateway settings.</param>
        public CloudCommunicationService(
            ILogger<CloudCommunicationService> logger,
            IOptions<GatewaySettings> settings,
            AlertService.AlertServiceClient alertClient)
        {
            _logger = logger;
            _settings = settings.Value;
            _alertClient = alertClient;

            HttpClientHandler handler = new HttpClientHandler();
            handler.ClientCertificates.Add(new X509Certificate2(_settings.CertificatePath, _settings.CertificatePassword));

            // Trust the cloud server's certificate (optional if using a trusted CA - we need to decide as a team)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            HttpClient httpClient = new HttpClient(handler);

            GrpcChannel channel = GrpcChannel.ForAddress(_settings.CloudServerAddress, new GrpcChannelOptions
            {
                HttpClient = httpClient
            });

            _client = new LicenseService.LicenseServiceClient(channel);
            _monitoringClient = new MonitoringService.MonitoringServiceClient(channel);

            // Configure retry policy using Polly
            _retryPolicy = Policy.Handle<RpcException>(ex => IsTransientFault(ex))
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning("Retry {retryCount} for gRPC call due to error: {message}", retryCount, exception.Message);
                    });
        }

        /// <summary>
        /// Registers a new device with the cloud web service.
        /// </summary>
        /// <param name="uuid">The device UUID.</param>
        /// <returns>A task representing the registration response.</returns>
        public async Task<DeviceRegistrationResponse> RegisterDeviceAsync(string uuid)
        {
            _logger.LogInformation("Sending device registration request to cloud for UUID: {uuid}", uuid);

            return await _retryPolicy.ExecuteAsync(async () =>
            {
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
            });
        }

        /// <summary>
        /// Renews the license for an existing device with the cloud web service.
        /// </summary>
        /// <param name="uuid">The device UUID.</param>
        /// <returns>A task representing the renewal response.</returns>
        public async Task<LicenseRenewalResponse> RenewLicenseAsync(string uuid)
        {
            _logger.LogInformation("Sending license renewal request to cloud for UUID: {uuid}", uuid);

            return await _retryPolicy.ExecuteAsync(async () =>
            {
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
            });
        }

        /// <summary>
        /// Determines if an exception is a transient fault.
        /// </summary>
        /// <param name="ex">The RpcException to evaluate.</param>
        /// <returns>True if the exception is transient; otherwise, false.</returns>
        private bool IsTransientFault(RpcException ex)
        {
            // Identify transient gRPC errors (like Unavailable, DeadlineExceeded)
            return ex.StatusCode == StatusCode.Unavailable || ex.StatusCode == StatusCode.DeadlineExceeded;
        }

        /// <summary>
        /// Sends aggregated monitoring data to the cloud web service.
        /// </summary>
        /// <param name="aggregatedDataList">The list of aggregated data to send.</param>
        /// <returns>A task representing the send operation.</returns>
        public async Task SendAggregatedDataAsync(List<Models.AggregatedData> aggregatedDataList)
        {
            _logger.LogInformation("Sending aggregated monitoring data to cloud.");

            await _retryPolicy.ExecuteAsync(async () =>
            {
                var request = new AggregatedDataRequest
                {
                    AggregatedDataList = { aggregatedDataList.Select(TransformToProtobufAggregatedData) }
                };

                var response = await _monitoringClient.SendAggregatedDataAsync(request);

                if (response.Success)
                {
                    _logger.LogInformation("Aggregated data sent successfully to cloud.");
                }
                else
                {
                    _logger.LogWarning("Cloud responded with failure: {message}", response.Message);
                }
            });
        }

        /// <summary>
        /// Transforms an AggregatedData object to the Protobuf-generated AggregatedData message.
        /// </summary>
        /// <param name="data">The aggregated data to transform.</param>
        /// <returns>The Protobuf AggregatedData message.</returns>
        private Protos.AggregatedData TransformToProtobufAggregatedData(Models.AggregatedData data)
        {
            return new Protos.AggregatedData
            {
                Uuid = data.UUID,
                Timestamp = Timestamp.FromDateTime(data.Timestamp),
                Metrics = new Protos.Metrics
                {
                    CpuUsage = data.Metrics.CpuUsage,
                    MemoryUsage = data.Metrics.MemoryUsage,
                    DiskUsage = data.Metrics.DiskUsage,
                    CpuTemperature = data.Metrics.CpuTemperature,
                    SystemUptime = data.Metrics.SystemUptime,
                    NetworkUsage = new Protos.NetworkUsage
                    {
                        BytesSent = data.Metrics.NetworkUsage.BytesSent,
                        BytesReceived = data.Metrics.NetworkUsage.BytesReceived
                    }
                },
                SensorData = new Protos.SensorData
                {
                    RedValue = data.SensorData.RedValue,
                    GreenValue = data.SensorData.GreenValue,
                    BlueValue = data.SensorData.BlueValue
                }
            };
        }

        /// <summary>
        /// Sends device logs to the cloud.
        /// </summary>
        /// <param name="deviceUuid">The UUID of the device.</param>
        /// <param name="logFilePath">The path to the log file.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task SendDeviceLogsAsync(string deviceUuid, string logFilePath)
        {
            _logger.LogInformation("Sending logs for device {uuid} to cloud.", deviceUuid);

            try
            {
                using AsyncClientStreamingCall<LogUploadRequest, LogUploadResponse> call = _cloudLogClient.UploadDeviceLogs();

                // Send metadata
                await call.RequestStream.WriteAsync(new LogUploadRequest
                {
                    Metadata = new LogMetadata
                    {
                        DeviceUuid = deviceUuid,
                        FileName = Path.GetFileName(logFilePath)
                    }
                });

                // Send log file in chunks
                byte[] buffer = new byte[64 * 1024]; // 64KB buffer
                using FileStream fileStream = File.OpenRead(logFilePath);
                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await call.RequestStream.WriteAsync(new LogUploadRequest
                    {
                        Data = Google.Protobuf.ByteString.CopyFrom(buffer, 0, bytesRead)
                    });
                }

                await call.RequestStream.CompleteAsync();

                LogUploadResponse response = await call.ResponseAsync;

                if (response.Success)
                {
                    _logger.LogInformation("Logs for device {uuid} sent successfully to cloud.", deviceUuid);
                }
                else
                {
                    _logger.LogWarning("Cloud responded with failure for device {uuid}: {message}", deviceUuid, response.Message);
                }
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC error during sending logs for device {uuid}.", deviceUuid);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during sending logs for device {uuid}.", deviceUuid);
                throw;
            }
        }

        /// <summary>
        /// Sends an alert to the cloud.
        /// </summary>
        /// <param name="alert">The alert to send.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task SendAlertAsync(Alert alert)
        {
            _logger.LogInformation("Sending alert to cloud: {message}", alert.Message);

            try
            {
                AlertRequest alertRequest = new AlertRequest
                {
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(alert.Timestamp),
                    Level = alert.Level,
                    Message = alert.Message,
                    Exception = alert.Exception ?? "",
                    Source = alert.Source
                };

                AlertResponse response = await _alertClient.SendAlertAsync(alertRequest);

                if (response.Success)
                {
                    _logger.LogInformation("Alert sent to cloud successfully.");
                }
                else
                {
                    _logger.LogWarning("Cloud responded with failure: {message}", response.Message);
                }
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC error during sending alert to cloud.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during sending alert to cloud.");
                throw;
            }
        }
    }
}
