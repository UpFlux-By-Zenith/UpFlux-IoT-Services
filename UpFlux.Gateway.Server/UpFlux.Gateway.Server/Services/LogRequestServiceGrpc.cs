using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.Extensions.Logging;
using UpFlux.Gateway.Server.Protos;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// gRPC service for handling log requests from the cloud.
    /// </summary>
    public class LogRequestServiceGrpc : LogRequestService.LogRequestServiceBase
    {
        private readonly ILogger<LogRequestServiceGrpc> _logger;
        private readonly LogCollectionService _logCollectionService;

        /// <summary>
        /// Initializes a new instance of the <see cref="LogRequestServiceGrpc"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="logCollectionService">Log collection service.</param>
        public LogRequestServiceGrpc(
            ILogger<LogRequestServiceGrpc> logger,
            LogCollectionService logCollectionService)
        {
            _logger = logger;
            _logCollectionService = logCollectionService;
        }

        /// <inheritdoc/>
        public override async Task<LogResponse> RequestDeviceLogs(LogRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Received log request from cloud for devices: {deviceUuids}", string.Join(", ", request.DeviceUuids));

            try
            {
                List<Task> tasks = new List<Task>();

                foreach (string? deviceUuid in request.DeviceUuids)
                {
                    tasks.Add(_logCollectionService.CollectAndSendLogsAsync(deviceUuid));
                }

                await Task.WhenAll(tasks);

                return new LogResponse
                {
                    Success = true,
                    Message = "Logs collected and sent to cloud successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during log collection.");
                return new LogResponse
                {
                    Success = false,
                    Message = "An error occurred during log collection."
                };
            }
        }
    }
}
