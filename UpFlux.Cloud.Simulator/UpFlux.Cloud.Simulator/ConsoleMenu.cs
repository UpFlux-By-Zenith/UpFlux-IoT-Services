using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Grpc.Core;
using System.Net.Http;
using UpFlux.Gateway.Server.Protos;

namespace UpFlux.Cloud.Simulator
{
    public class ConsoleMenu
    {
        private readonly IConfiguration _configuration;
        private readonly CloudSettings _cloudSettings;

        public ConsoleMenu(IConfiguration configuration)
        {
            _configuration = configuration;
            _cloudSettings = configuration.GetSection("CloudSettings").Get<CloudSettings>();
        }

        public async Task RunMenuLoop()
        {
            while (true)
            {
                ConsoleSync.WriteLine("\n--- UpFlux Cloud Simulator Menu (calls Gateway) ---");
                ConsoleSync.WriteLine("1) Send ROLLBACK command to device(s)");
                ConsoleSync.WriteLine("2) Request logs from device(s)");
                ConsoleSync.WriteLine("3) Send update package to device(s)");
                ConsoleSync.WriteLine("4) Request version info from device(s)");
                ConsoleSync.WriteLine("5) Exit");
                ConsoleSync.Write("Choose: ");
                char key = ConsoleSync.ReadKey();
                ConsoleSync.WriteLine("");

                if (key == '1')
                {
                    await MenuSendRollback();
                }
                else if (key == '2')
                {
                    await MenuRequestLogs();
                }
                else if (key == '3')
                {
                    await MenuSendUpdate();
                }
                else if (key == '4')
                {
                    await MenuRequestVersionData();
                }
                else if (key == '5')
                {
                    ConsoleSync.WriteLine("Exiting menu...");
                    break;
                }
                else
                {
                    ConsoleSync.WriteLine("Invalid choice. Try again.");
                }
            }
        }

        private GrpcChannel CreateGatewayChannel()
        {
            // Build channel for connecting to the Gateway
            //HttpClientHandler httpHandler = new HttpClientHandler();
            //if (_cloudSettings.SkipServerCertificateValidation)
            //{
            //    httpHandler.ServerCertificateCustomValidationCallback =
            //        (req, cert, chain, errors) => true;
            //}

            //GrpcChannel channel = GrpcChannel.ForAddress(
            //    _cloudSettings.GatewayAddress,
            //    new Grpc.Net.Client.GrpcChannelOptions { HttpHandler = httpHandler });

            // Use HTTP for the gateway address
            GrpcChannel channel = GrpcChannel.ForAddress(
                _cloudSettings.GatewayAddress,
                new GrpcChannelOptions()
            );

            return channel;
        }

        private async Task MenuSendRollback()
        {
            ConsoleSync.Write("Enter device UUID(s), comma-separated: ");
            string line = ConsoleSync.ReadLine() ?? "";
            string[] uuids = line.Split(',', StringSplitOptions.RemoveEmptyEntries);

            ConsoleSync.Write("Enter rollback parameter (e.g. version=1.2.3): ");
            string param = ConsoleSync.ReadLine() ?? "version=1.0.0";

            using GrpcChannel channel = CreateGatewayChannel();
            CommandService.CommandServiceClient commandClient = new CommandService.CommandServiceClient(channel);

            CommandRequest request = new CommandRequest
            {
                CommandId = Guid.NewGuid().ToString(),
                CommandType = CommandType.Rollback,
                Parameters = param
            };
            request.TargetDevices.AddRange(uuids);

            ConsoleSync.WriteLine($"Sending rollback command to the Gateway for {uuids.Length} device(s)...");
            await commandClient.SendCommandAsync(request);
            ConsoleSync.WriteLine("Rollback command sent.");
        }

        private async Task MenuRequestLogs()
        {
            ConsoleSync.Write("Enter device UUID(s), comma-separated: ");
            string line = ConsoleSync.ReadLine() ?? "";
            string[] uuids = line.Split(',', StringSplitOptions.RemoveEmptyEntries);

            using GrpcChannel channel = CreateGatewayChannel();
            LogRequestService.LogRequestServiceClient logReqClient = new LogRequestService.LogRequestServiceClient(channel);

            LogRequest request = new LogRequest();
            request.DeviceUuids.AddRange(uuids);

            ConsoleSync.WriteLine("Requesting logs from the Gateway...");
            LogResponse response = await logReqClient.RequestDeviceLogsAsync(request);
            ConsoleSync.WriteLine($"LogRequest result => success={response.Success}, message={response.Message}");
        }

        private async Task MenuSendUpdate()
        {
            ConsoleSync.Write("Enter device UUID(s), comma-separated: ");
            string line = ConsoleSync.ReadLine() ?? "";
            string[] uuids = line.Split(',', StringSplitOptions.RemoveEmptyEntries);

            ConsoleSync.Write("Path to .deb package: ");
            string packagePath = ConsoleSync.ReadLine() ?? "";
            if (!File.Exists(packagePath))
            {
                ConsoleSync.WriteLine("File not found. Aborting.");
                return;
            }
            string fileName = Path.GetFileName(packagePath);

            // read bytes
            byte[] packageData = await File.ReadAllBytesAsync(packagePath);

            using GrpcChannel channel = CreateGatewayChannel();
            UpdateService.UpdateServiceClient updateClient = new UpdateService.UpdateServiceClient(channel);

            UpdatePackageRequest request = new UpdatePackageRequest
            {
                FileName = fileName,
                PackageData = Google.Protobuf.ByteString.CopyFrom(packageData)
            };
            request.TargetDevices.AddRange(uuids);

            ConsoleSync.WriteLine($"Sending update package '{fileName}' to Gateway for {uuids.Length} device(s)...");
            await updateClient.SendUpdatePackageAsync(request);
            ConsoleSync.WriteLine("Update package sent.");
        }

        private async Task MenuRequestVersionData()
        {
            // calls the Gateway's VersionDataService
            using GrpcChannel channel = CreateGatewayChannel();
            VersionDataService.VersionDataServiceClient versionClient = new VersionDataService.VersionDataServiceClient(channel);

            ConsoleSync.WriteLine("Requesting version data from Gateway...");

            VersionDataRequest request = new VersionDataRequest();
            VersionDataResponse resp = await versionClient.RequestVersionDataAsync(request);

            ConsoleSync.WriteLine($"Result => success={resp.Success}, message={resp.Message}");
            foreach (DeviceVersions? devVers in resp.DeviceVersionsList)
            {
                ConsoleSync.WriteLine($" Device={devVers.DeviceUuid}");
                foreach (Gateway.Server.Protos.VersionInfo? ver in devVers.Versions)
                {
                    ConsoleSync.WriteLine($"   Version={ver.Version}, InstalledAt={ver.InstalledAt}");
                }
            }
        }
    }
}
