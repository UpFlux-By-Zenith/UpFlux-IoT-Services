using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using UpFlux.Gateway.Server.Protos;

namespace UpFlux.Cloud.Simulator
{
    public class AlertServiceMock : AlertService.AlertServiceBase
    {
        private readonly ILogger<AlertServiceMock> _logger;

        public AlertServiceMock(ILogger<AlertServiceMock> logger)
        {
            _logger = logger;
        }

        public override Task<AlertResponse> SendAlert(AlertRequest request, ServerCallContext context)
        {
            _logger.LogInformation("CloudSim ALERT from {0} | Level={1} | Msg={2}",
                request.Source, request.Level, request.Message);

            return Task.FromResult(new AlertResponse
            {
                Success = true,
                Message = "CloudSim: alert received"
            });
        }
    }
}
