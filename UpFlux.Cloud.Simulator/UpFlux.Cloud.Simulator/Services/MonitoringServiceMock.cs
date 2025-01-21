using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using UpFlux.Gateway.Server.Protos;

namespace UpFlux.Cloud.Simulator
{
    public class MonitoringServiceMock : MonitoringService.MonitoringServiceBase
    {
        private readonly ILogger<MonitoringServiceMock> _logger;

        public MonitoringServiceMock(ILogger<MonitoringServiceMock> logger)
        {
            _logger = logger;
        }

        public override Task<AggregatedDataResponse> SendAggregatedData(AggregatedDataRequest request, ServerCallContext context)
        {
            _logger.LogInformation("CloudSim: Received {count} AggregatedData items.", request.AggregatedDataList.Count);

            foreach (AggregatedData? data in request.AggregatedDataList)
            {
                _logger.LogInformation(" -> Device={0}, CPU={1}% Mem={2}%",
                    data.Uuid, data.Metrics.CpuUsage, data.Metrics.MemoryUsage);
            }

            AggregatedDataResponse resp = new AggregatedDataResponse
            {
                Success = true,
                Message = "CloudSim: Data logged"
            };
            return Task.FromResult(resp);
        }
    }
}
