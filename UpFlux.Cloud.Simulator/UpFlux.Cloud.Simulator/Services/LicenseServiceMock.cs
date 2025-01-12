using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using UpFlux.Gateway.Server.Protos;

namespace UpFlux.Cloud.Simulator
{
    public class LicenseServiceMock : LicenseService.LicenseServiceBase
    {
        private readonly ILogger<LicenseServiceMock> _logger;

        public LicenseServiceMock(ILogger<LicenseServiceMock> logger)
        {
            _logger = logger;
        }

        public override Task<DeviceRegistrationResponse> RegisterDevice(DeviceRegistrationRequest request, ServerCallContext context)
        {
            _logger.LogInformation("CloudSim: RegisterDevice called for UUID={0}", request.Uuid);

            Console.WriteLine($"\n[LicenseService] RegisterDevice for {request.Uuid}. Approve? (y/n)");
            char key = Console.ReadKey(intercept: true).KeyChar;
            Console.WriteLine();
            bool approved = (key == 'y' || key == 'Y');

            DeviceRegistrationResponse response = new DeviceRegistrationResponse
            {
                Approved = approved,
                License = approved ? "MOCK_LICENSE_ABC" : "",
                ExpirationDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddMonths(1))
            };
            return Task.FromResult(response);
        }

        public override Task<LicenseRenewalResponse> RenewLicense(LicenseRenewalRequest request, ServerCallContext context)
        {
            _logger.LogInformation("CloudSim: RenewLicense called for UUID={0}", request.Uuid);

            Console.WriteLine($"\n[LicenseService] RenewLicense for {request.Uuid}. Approve? (y/n)");
            char key = Console.ReadKey(intercept: true).KeyChar;
            Console.WriteLine();
            bool approved = (key == 'y' || key == 'Y');

            LicenseRenewalResponse response = new LicenseRenewalResponse
            {
                Approved = approved,
                License = approved ? "MOCK_LICENSE_RENEWED" : "",
                ExpirationDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddMonths(2))
            };
            return Task.FromResult(response);
        }
    }
}
