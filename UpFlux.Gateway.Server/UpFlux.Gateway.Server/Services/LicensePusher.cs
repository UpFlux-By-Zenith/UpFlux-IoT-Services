using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// A specialized class that sends licenses to devices,
    /// internally using <see cref="DeviceCommunicationService"/>.
    /// </summary>
    public class LicensePusher : ILicensePusher
    {
        private readonly ILogger<LicensePusher> _logger;
        private readonly DeviceCommunicationService _deviceComm;

        public LicensePusher(ILogger<LicensePusher> logger, DeviceCommunicationService deviceComm)
        {
            _logger = logger;
            _deviceComm = deviceComm;
        }

        /// <inheritdoc/>
        public async Task<bool> SendLicenseAsync(string uuid, string license)
        {
            _logger.LogInformation("LicensePusher: Sending license to device {uuid}", uuid);
            return await _deviceComm.SendLicenseToDeviceAsync(uuid, license);
        }
    }
}
