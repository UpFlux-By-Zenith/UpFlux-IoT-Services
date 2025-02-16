using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Periodically calls AI (DBSCAN and Scheduling) and sends results to the Cloud 
    /// for the "clustering page".
    /// </summary>
    public class AiIntegrationWorker : BackgroundService
    {
        private readonly ILogger<AiIntegrationWorker> _logger;
        private readonly AiCommunicationService _aiService;
        private readonly ControlChannelWorker _controlChannelWorker;

        public AiIntegrationWorker(
            ILogger<AiIntegrationWorker> logger,
            AiCommunicationService aiService,
            ControlChannelWorker controlChannelWorker
        )
        {
            _logger = logger;
            _aiService = aiService;
            _controlChannelWorker = controlChannelWorker;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Run clustering
                    AiClusteringResult clusters = await _aiService.RunClusteringAsync();
                    if (clusters != null)
                    {
                        // Run scheduling
                        AiSchedulingResult schedule = await _aiService.RunSchedulingAsync(clusters);

                        if (schedule != null)
                        {
                            // send to Cloud
                            await _controlChannelWorker.SendAiRecommendationsAsync(clusters, schedule);

                            _logger.LogInformation("Sent AI clusters + scheduling to Cloud.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in AiIntegrationWorker loop.");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
