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
                Console.WriteLine("\n--- UpFlux Cloud Simulator Menu (calls Gateway) ---");
                Console.WriteLine("1) Send ROLLBACK command to device(s)");
                Console.WriteLine("2) Request logs from device(s)");
                Console.WriteLine("3) Send update package to device(s)");
                Console.WriteLine("4) Request version info from device(s)");
                Console.WriteLine("5) Exit");
                Console.Write("Choose: ");
                char key = Console.ReadKey(intercept: true).KeyChar;
                Console.WriteLine();

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
                    Console.WriteLine("Exiting menu...");
                    break;
                }
                else
                {
                    Console.WriteLine("Invalid choice. Try again.");
                }
            }
        }

        private GrpcChannel CreateGatewayChannel()
        {
            // Build channel for connecting to the Gateway
            HttpClientHandler httpHandler = new HttpClientHandler();
            if (_cloudSettings.SkipServerCertificateValidation)
            {
                httpHandler.ServerCertificateCustomValidationCallback =
                    (req, cert, chain, errors) => true;
            }

            GrpcChannel channel = GrpcChannel.ForAddress(
                _cloudSettings.GatewayAddress,
                new Grpc.Net.Client.GrpcChannelOptions { HttpHandler = httpHandler });
            return channel;
        }

        private async Task MenuSendRollback()
        {
            Console.Write("Enter device UUID(s), comma-separated: ");
            string line = Console.ReadLine() ?? "";
            string[] uuids = line.Split(',', StringSplitOptions.RemoveEmptyEntries);

            Console.Write("Enter rollback parameter (e.g. version=1.2.3): ");
            string param = Console.ReadLine() ?? "version=1.0.0";

            using GrpcChannel channel = CreateGatewayChannel();
            CommandService.CommandServiceClient commandClient = new CommandService.CommandServiceClient(channel);

            CommandRequest request = new CommandRequest
            {
                CommandId = Guid.NewGuid().ToString(),
                CommandType = CommandType.Rollback,
                Parameters = param
            };
            request.TargetDevices.AddRange(uuids);

            Console.WriteLine($"Sending rollback command to the Gateway for {uuids.Length} device(s)...");
            await commandClient.SendCommandAsync(request);
            Console.WriteLine("Rollback command sent.");
        }

        private async Task MenuRequestLogs()
        {
            Console.Write("Enter device UUID(s), comma-separated: ");
            string line = Console.ReadLine() ?? "";
            string[] uuids = line.Split(',', StringSplitOptions.RemoveEmptyEntries);

            using GrpcChannel channel = CreateGatewayChannel();
            LogRequestService.LogRequestServiceClient logReqClient = new LogRequestService.LogRequestServiceClient(channel);

            LogRequest request = new LogRequest();
            request.DeviceUuids.AddRange(uuids);

            Console.WriteLine("Requesting logs from the Gateway...");
            LogResponse response = await logReqClient.RequestDeviceLogsAsync(request);
            Console.WriteLine($"LogRequest result => success={response.Success}, message={response.Message}");
        }

        private async Task MenuSendUpdate()
        {
            Console.Write("Enter device UUID(s), comma-separated: ");
            string line = Console.ReadLine() ?? "";
            string[] uuids = line.Split(',', StringSplitOptions.RemoveEmptyEntries);

            Console.Write("Path to .deb package: ");
            string packagePath = Console.ReadLine() ?? "";
            if (!File.Exists(packagePath))
            {
                Console.WriteLine("File not found. Aborting.");
                return;
            }
            string fileName = Path.GetFileName(packagePath);

            Console.Write("Enter version for this update: ");
            string version = Console.ReadLine() ?? "1.0.0";

            string packageId = Guid.NewGuid().ToString();

            // read bytes
            byte[] packageData = await File.ReadAllBytesAsync(packagePath);

            // we haven't created no real signature for now
            byte[] signature = new byte[0];

            using GrpcChannel channel = CreateGatewayChannel();
            UpdateService.UpdateServiceClient updateClient = new UpdateService.UpdateServiceClient(channel);

            UpdatePackageRequest request = new UpdatePackageRequest
            {
                PackageId = packageId,
                Version = version,
                FileName = fileName,
                PackageData = Google.Protobuf.ByteString.CopyFrom(packageData),
                Signature = Google.Protobuf.ByteString.CopyFrom(signature)
            };
            request.TargetDevices.AddRange(uuids);

            Console.WriteLine($"Sending update package {packageId} to Gateway for {uuids.Length} device(s)...");
            await updateClient.SendUpdatePackageAsync(request);
            Console.WriteLine("Update package sent.");
        }

        private async Task MenuRequestVersionData()
        {
            // calls the Gateway's VersionDataService
            using GrpcChannel channel = CreateGatewayChannel();
            VersionDataService.VersionDataServiceClient versionClient = new VersionDataService.VersionDataServiceClient(channel);

            Console.WriteLine("Requesting version data from Gateway...");

            VersionDataRequest request = new VersionDataRequest();
            VersionDataResponse resp = await versionClient.RequestVersionDataAsync(request);

            Console.WriteLine($"Result => success={resp.Success}, message={resp.Message}");
            foreach (DeviceVersions? devVers in resp.DeviceVersionsList)
            {
                Console.WriteLine($" Device={devVers.DeviceUuid}");
                foreach (Gateway.Server.Protos.VersionInfo? ver in devVers.Versions)
                {
                    Console.WriteLine($"   Version={ver.Version}, InstalledAt={ver.InstalledAt}");
                }
            }
        }
    }
}
