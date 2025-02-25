using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UpFlux.Gateway.Server.Models;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Worker that processes scheduled updates for devices.
    /// </summary>
    public class ScheduledUpdateWorker : BackgroundService
    {
        private readonly ILogger<ScheduledUpdateWorker> _logger;
        private readonly ControlChannelWorker _controlChannelWorker;
        private readonly UpdateManagementService _updateManagementService;

        public ScheduledUpdateWorker(
            ILogger<ScheduledUpdateWorker> logger,
            ControlChannelWorker controlChannelWorker,
            UpdateManagementService updateManagementService
        )
        {
            _logger = logger;
            _controlChannelWorker = controlChannelWorker;
            _updateManagementService = updateManagementService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    List<ScheduledUpdateEntry> dueList = new List<ScheduledUpdateEntry>();
                    lock (_controlChannelWorker.ScheduledUpdates)
                    {
                        DateTime now = DateTime.UtcNow;
                        List<ScheduledUpdateEntry> all = _controlChannelWorker.ScheduledUpdates.Values.ToList();
                        foreach (var entry in all)
                        {
                            if (entry.StartTimeUtc <= now)
                            {
                                dueList.Add(entry);
                            }
                        }
                    }

                    // process due
                    foreach (ScheduledUpdateEntry entry in dueList)
                    {
                        _logger.LogInformation(
                            "Time to apply scheduled update {0}, for devices={1}, start={2}",
                            entry.ScheduleId, string.Join(",", entry.DeviceUuids),
                            entry.StartTimeUtc.ToString("o"));

                        // create a temp file with the packageData
                        string tempFile = Path.GetTempFileName();
                        await File.WriteAllBytesAsync(tempFile, entry.PackageData);

                        UpdatePackage up = new Models.UpdatePackage
                        {
                            FileName = entry.FileName,
                            FilePath = tempFile,
                            ReceivedAt = DateTime.UtcNow,
                            TargetDevices = entry.DeviceUuids.ToArray()
                        };

                        // Use existing logic
                        Dictionary<string, bool> results = await _updateManagementService.HandleUpdatePackageAsync(up);

                        // remove from dictionary after applying
                        lock (_controlChannelWorker.ScheduledUpdates)
                        {
                            if (_controlChannelWorker.ScheduledUpdates.ContainsKey(entry.ScheduleId))
                                _controlChannelWorker.ScheduledUpdates.Remove(entry.ScheduleId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ScheduledUpdateWorker loop");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
