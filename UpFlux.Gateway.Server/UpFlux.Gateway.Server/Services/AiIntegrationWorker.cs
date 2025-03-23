using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Gateway.Server.Repositories;

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
        private readonly DeviceRepository _deviceRepository;
        private readonly DeviceUsageAggregator _usageAggregator;

        public AiIntegrationWorker(
            ILogger<AiIntegrationWorker> logger,
            AiCommunicationService aiService,
            ControlChannelWorker controlChannelWorker,
            DeviceRepository deviceRepository,
            DeviceUsageAggregator usageAggregator
        )
        {
            _logger = logger;
            _aiService = aiService;
            _controlChannelWorker = controlChannelWorker;
            _deviceRepository = deviceRepository;
            _usageAggregator = usageAggregator;
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
                        // Build aggregatorData from aggregator's predicted idle window
                        List<Models.Device> devices = _deviceRepository.GetAllDevices();
                        List<object> aggregatorDataList = new List<object>();

                        foreach (Models.Device dev in devices)
                        {
                            DeviceIdleInfo idleInfo = _usageAggregator.PredictNextIdleWindow(dev.UUID);
                            if (idleInfo.NextIdleTime.HasValue && idleInfo.IdleDurationSecs >= 20)
                            {
                                aggregatorDataList.Add(new
                                {
                                    deviceUuid = dev.UUID,
                                    nextIdleTime = idleInfo.NextIdleTime.Value.ToString("o"),
                                    idleDurationSecs = idleInfo.IdleDurationSecs
                                });
                            }
                            else
                            {
                                // not enough idle
                                aggregatorDataList.Add(new
                                {
                                    deviceUuid = dev.UUID,
                                    nextIdleTime = (string)null,
                                    idleDurationSecs = 0
                                });
                            }
                        }

                        // Run scheduling
                        AiSchedulingResult schedule = await _aiService.RunSchedulingAsync(clusters, aggregatorDataList);

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
