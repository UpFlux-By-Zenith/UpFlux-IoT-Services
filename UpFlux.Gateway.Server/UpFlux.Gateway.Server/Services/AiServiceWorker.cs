using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Background worker that starts and stops the AI Service.
    /// </summary>
    public class AiServiceWorker : BackgroundService
    {
        private readonly ILogger<AiServiceWorker> _logger;
        private readonly AiServiceRunner _aiServiceRunner;

        public AiServiceWorker(ILogger<AiServiceWorker> logger, AiServiceRunner aiServiceRunner)
        {
            _logger = logger;
            _aiServiceRunner = aiServiceRunner;
        }

        /// <summary>
        /// Starts the AI service when the worker runs.
        /// </summary>
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AI Service Worker starting...");
            _aiServiceRunner.StartAiService();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops the AI service on shutdown.
        /// </summary>
        public override Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AI Service Worker stopping...");
            _aiServiceRunner.StopAiService();
            return base.StopAsync(stoppingToken);
        }
    }
}
