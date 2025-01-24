using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using UpFlux.Gateway.Server.Protos;
using Google.Protobuf;

namespace UpFlux.Cloud.Simulator
{
    public class CloudLogServiceMock : CloudLogService.CloudLogServiceBase
    {
        private readonly ILogger<CloudLogServiceMock> _logger;

        public CloudLogServiceMock(ILogger<CloudLogServiceMock> logger)
        {
            _logger = logger;
        }

        public override async Task<LogUploadResponse> UploadDeviceLogs(IAsyncStreamReader<LogUploadRequest> requestStream, ServerCallContext context)
        {
            string deviceUuid = null;
            string fileName = null;
            using MemoryStream ms = new MemoryStream();

            while (await requestStream.MoveNext())
            {
                LogUploadRequest current = requestStream.Current;
                if (current.Metadata != null)
                {
                    deviceUuid = current.Metadata.DeviceUuid;
                    fileName = current.Metadata.FileName;
                    _logger.LogInformation("CloudSim: Device {0} sending logs => {1}", deviceUuid, fileName);
                }
                else if (current.Data != null)
                {
                    ms.Write(current.Data.ToByteArray());
                }
            }

            _logger.LogInformation("CloudSim: Received {0} bytes from device {1}", ms.Length, deviceUuid);

            Directory.CreateDirectory("CloudLogs");
            string path = Path.Combine("CloudLogs", fileName);
            File.WriteAllBytes(path, ms.ToArray());
            _logger.LogInformation("Logs saved to {0}", path);

            return new LogUploadResponse
            {
                Success = true,
                Message = "CloudSim: log file stored"
            };
        }
    }
}
