using UpFlux.Gateway.Server.Services;

namespace UpFlux.Gateway.Server
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly DeviceCommunicationService _deviceCommunicationService;
        private bool _isListening;

        public Worker(ILogger<Worker> logger, DeviceCommunicationService deviceCommunicationService)
        {
            _logger = logger;
            _deviceCommunicationService = deviceCommunicationService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Ensure StartListening is called only once
            if (!_isListening)
            {
                _deviceCommunicationService.StartListening();
                _isListening = true;
                _logger.LogInformation("DeviceCommunicationService started listening for connections.");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(1000, stoppingToken); // Avoid unnecessary CPU usage
            }
        }
    }
}
