using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UpFlux.Gateway.Server.Models;
using System.Collections.Concurrent;
using Grpc.Core;
using UpFlux.Gateway.Server.Protos;
using Microsoft.Extensions.Options;
using Grpc.Net.Client;
using Google.Protobuf.WellKnownTypes;
using Google.Protobuf;
using UpFlux.Gateway.Server.Repositories;
using System.Diagnostics;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// A single persistent background worker that connects to the Cloud's
    /// ControlChannel service. 
    /// 
    /// - Opens the bidirectional stream
    /// - Sends messages (LicenseRequest, MonitoringData, LogUpload, etc.)
    /// - Receives messages (LicenseResponse, CommandRequest, etc.)
    /// - Dispatches them to/from local services
    /// </summary>
    public class ControlChannelWorker : BackgroundService
    {
        private readonly ILogger<ControlChannelWorker> _logger;
        private readonly GatewaySettings _gatewaySettings;

        private readonly DeviceRepository _deviceRepository;
        private readonly DeviceCommunicationService _deviceCommunicationService;
        private readonly CommandExecutionService _commandExecutionService;
        private readonly LogCollectionService _logCollectionService;
        private readonly UpdateManagementService _updateManagementService;
        private readonly AlertingService _alertingService;

        // A concurrent dictionary to keep track of license requests in flight
        private ConcurrentDictionary<string, bool> _licenseRequestsInFlight = new ConcurrentDictionary<string, bool>();

        // The gRPC channel to the Cloud
        private IClientStreamWriter<ControlMessage> _requestStream;

        // A dictionary to keep track of scheduled updates
        private Dictionary<string, ScheduledUpdateEntry> _scheduledUpdates = new Dictionary<string, ScheduledUpdateEntry>();

        public Dictionary<string, ScheduledUpdateEntry> ScheduledUpdates => _scheduledUpdates;

        /// <summary>
        /// The constructor for the ControlChannelWorker.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="gatewaySettings"></param>
        /// <param name="deviceCommunicationService"></param>
        /// <param name="commandExecutionService"></param>
        /// <param name="logCollectionService"></param>
        /// <param name="updateManagementService"></param>
        /// <param name="alertingService"></param>
        public ControlChannelWorker(
            ILogger<ControlChannelWorker> logger,
            IOptions<GatewaySettings> gatewaySettings,
            DeviceRepository deviceRepository,
            DeviceCommunicationService deviceCommunicationService,
            CommandExecutionService commandExecutionService,
            LogCollectionService logCollectionService,
            UpdateManagementService updateManagementService,
            AlertingService alertingService)
        {
            _logger = logger;
            _gatewaySettings = gatewaySettings.Value;
            _deviceRepository = deviceRepository;
            _deviceCommunicationService = deviceCommunicationService;
            _commandExecutionService = commandExecutionService;
            _logCollectionService = logCollectionService;
            _updateManagementService = updateManagementService;

            // Subscribe to alerts from AlertingService
            alertingService.OnAlertGenerated += async (alert) => await SendAlertAsync(alert);
        }

        /// <summary>
        /// To execute the ControlChannelWorker, we need to connect to the Cloud's ControlChannel service.
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Build a channel
                    using GrpcChannel channel = GrpcChannel.ForAddress(_gatewaySettings.CloudServerAddress, new GrpcChannelOptions
                    {
                        MaxReceiveMessageSize = 200 * 1024 * 1024,
                        MaxSendMessageSize = 200 * 1024 * 1024
                    });
                    ControlChannel.ControlChannelClient client = new ControlChannel.ControlChannelClient(channel);

                    // Open the stream
                    using AsyncDuplexStreamingCall<ControlMessage, ControlMessage> call = client.OpenControlChannel();

                    // store the requestStream so we can push messages 
                    _requestStream = call.RequestStream;

                    // send an initial message to identify this gateway server
                    ControlMessage initMsg = new ControlMessage
                    {
                        SenderId = _gatewaySettings.GatewayId,
                    };
                    await _requestStream.WriteAsync(initMsg);

                    _logger.LogInformation("Connected to Cloud ControlChannel, entering read loop...");

                    // read in a loop
                    Task readTask = ReadLoop(call.ResponseStream, stoppingToken);

                    // wait until readLoop finishes (cloud disconnect) or we get canceled
                    await readTask;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ControlChannelWorker. Retrying in 5s...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        /// <summary>
        /// Reads messages from the Cloud and dispatches them to the appropriate local service.
        /// </summary>
        /// <param name="responseStream">The stream of messages from the Cloud.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns></returns>
        private async Task ReadLoop(IAsyncStreamReader<ControlMessage> responseStream, CancellationToken token)
        {
            while (await responseStream.MoveNext(token))
            {
                ControlMessage msg = responseStream.Current;
                await HandleInboundMessage(msg);
            }
            _logger.LogWarning("Cloud closed the control channel or lost connection.");
        }

        /// <summary>
        /// Handles an inbound message from the Cloud.
        /// </summary>
        private async Task HandleInboundMessage(ControlMessage msg)
        {
            switch (msg.PayloadCase)
            {
                case ControlMessage.PayloadOneofCase.LicenseResponse:
                    await HandleLicenseResponse(msg.LicenseResponse);
                    break;

                case ControlMessage.PayloadOneofCase.CommandRequest:
                    await HandleCommandRequest(msg.CommandRequest);
                    break;

                case ControlMessage.PayloadOneofCase.LogRequest:
                    await HandleLogRequest(msg.LogRequest);
                    break;

                case ControlMessage.PayloadOneofCase.UpdatePackage:
                    await HandleUpdatePackage(msg.UpdatePackage);
                    break;

                case ControlMessage.PayloadOneofCase.VersionDataRequest:
                    await HandleVersionDataRequest(msg.VersionDataRequest);
                    break;

                case ControlMessage.PayloadOneofCase.AlertMessage:
                    _logger.LogInformation("Received an alert from Cloud? Usually it's Gateway→Cloud. Ignoring...");
                    break;

                case ControlMessage.PayloadOneofCase.ScheduledUpdate:
                    await HandleScheduledUpdate(msg.ScheduledUpdate);
                    break;

                default:
                    _logger.LogWarning("Unhandled message type from Cloud: {0}", msg.PayloadCase);
                    break;
            }
        }

        /// <summary>
        /// Handles a LicenseResponse message from the Cloud.
        /// </summary>
        private async Task HandleLicenseResponse(LicenseResponse resp)
        {
            _logger.LogInformation("Received LicenseResponse: device={0}, approved={1}", resp.DeviceUuid, resp.Approved);

            // Mark that we got a response
            _licenseRequestsInFlight.TryRemove(resp.DeviceUuid, out _);

            if (resp.Approved)
            {
                // We can store it in the DB and also push to device
                Device device = _deviceCommunicationService.SetDeviceLicense(resp.DeviceUuid, resp.License, resp.ExpirationDate.ToDateTime());
                if (device != null)
                {
                    _logger.LogInformation("License stored for device {0}, expiration={1}", device.UUID, device.LicenseExpiration);
                    await _deviceCommunicationService.SendLicenseToDeviceAsync(device.UUID);
                }
            }
            else
            {
                // Not approved so set NextEarliestRenewalAttempt
                _deviceCommunicationService.SetLicenseNotApproved(resp.DeviceUuid);
            }
        }

        /// <summary>
        /// Processes a CommandRequest from the Cloud.
        /// </summary>
        private async Task HandleCommandRequest(CommandRequest req)
        {
            _logger.LogInformation("Received CommandRequest from Cloud: Id={0}, type={1}, {2} target devices",
                req.CommandId, req.CommandType, req.TargetDevices.Count);

            // Create Command model
            Command command = new Command
            {
                CommandId = req.CommandId,
                CommandType = MapCommandType(req.CommandType),
                TargetDevices = req.TargetDevices.ToArray(),
                Parameters = req.Parameters,
                ReceivedAt = DateTime.UtcNow
            };

            CommandStatus status = null;
            string details = "";
            if (req.CommandType == CommandType.Rollback)
            {
                status = await _commandExecutionService.HandleCommandAsync(command);

                if (status == null)
                {
                    details = "Rollback encountered an error or was invalid.";
                }
                else
                {
                    bool anyFailed = (status.DevicesFailed.Count > 0);
                    if (!anyFailed)
                    {
                        details = $"Rollback succeeded on {string.Join(", ", status.DevicesSucceeded)}";
                    }
                    else
                    {
                        details = $"Rollback partial success: succeeded on {string.Join(", ", status.DevicesSucceeded)}, failed on {string.Join(", ", status.DevicesFailed)}";
                    }
                }
            }
            else
            {
                details = "Unknown command type";
            }

            bool overallSuccess = (status != null && status.DevicesFailed.Count == 0);

            // send back a CommandResponse
            ControlMessage msg = new ControlMessage
            {
                SenderId = _gatewaySettings.GatewayId,
                CommandResponse = new CommandResponse
                {
                    CommandId = req.CommandId,
                    Success = overallSuccess,
                    Details = details
                }
            };
            await _requestStream.WriteAsync(msg);
        }

        /// <summary>
        /// Handles a LogRequest from the Cloud.
        /// </summary>
        private async Task HandleLogRequest(LogRequestMessage req)
        {
            _logger.LogInformation("Cloud requests logs for {0} device(s)", req.DeviceUuids.Count);

            // We gather logs from each device, then stream them back as LogUpload messages
            foreach (string? deviceUuid in req.DeviceUuids)
            {
                string[] logPaths = await _logCollectionService.CollectLogsFromDeviceAsync(deviceUuid);
                // now push them up
                if (logPaths != null)
                {
                    foreach (string path in logPaths)
                    {
                        if (!File.Exists(path)) continue;
                        byte[] data = await File.ReadAllBytesAsync(path);
                        ControlMessage upload = new ControlMessage
                        {
                            SenderId = _gatewaySettings.GatewayId,
                            LogUpload = new LogUpload
                            {
                                DeviceUuid = deviceUuid,
                                FileName = Path.GetFileName(path),
                                Data = ByteString.CopyFrom(data)
                            }
                        };
                        await _requestStream.WriteAsync(upload);
                    }
                }
            }

            // Send a LogResponse so the Cloud is notified that all logs have been uploaded
            ControlMessage resp = new ControlMessage
            {
                SenderId = _gatewaySettings.GatewayId,
                LogResponse = new LogResponseMessage
                {
                    Success = true,
                    Message = "All logs uploaded"
                }
            };
            await _requestStream.WriteAsync(resp);
        }

        /// <summary>
        /// Verifies and Passes the UpdatePackage to the UpdateManagementService for processing.
        /// </summary>
        private async Task HandleUpdatePackage(Protos.UpdatePackage pkg)
        {
            _logger.LogInformation("Received UpdatePackage from Cloud: file={0}, size={1}, {2} target devices",
                pkg.FileName, pkg.PackageData.Length, pkg.TargetDevices.Count);

            // Save the package data to a temporary file
            string tempFilePath = Path.GetTempFileName();
            string signatureFilePath = tempFilePath + ".sig";

            await File.WriteAllBytesAsync(tempFilePath, pkg.PackageData.ToByteArray());
            await File.WriteAllBytesAsync(signatureFilePath, pkg.SignatureData.ToByteArray());

            bool verified = VerifySignature(tempFilePath, signatureFilePath);
            if (!verified) {
                _logger.LogError("Signature verification failed for package '{file}'", pkg.FileName);
                return;
            }

            _logger.LogInformation("Signature verification succeeded. Proceeding with distribution.");

            // Create UpdatePackage model
            Models.UpdatePackage updatePackage = new Models.UpdatePackage
            {
                FileName = pkg.FileName,
                FilePath = tempFilePath,
                TargetDevices = pkg.TargetDevices.ToArray(),
                ReceivedAt = DateTime.UtcNow
            };

            Dictionary<string, bool> results = await _updateManagementService.HandleUpdatePackageAsync(updatePackage);

            List<string> succeededDevices = new List<string>();
            List<string> failedDevices = new List<string>();
            foreach (KeyValuePair<string, bool> kvp in results)
            {
                if (kvp.Value) succeededDevices.Add(kvp.Key);
                else failedDevices.Add(kvp.Key);
            }

            bool overallSuccess = (failedDevices.Count == 0);

            // Create a detail string that enumerates success/fail
            string detailMsg = $"Succeeded on: {string.Join(", ", succeededDevices)}; Failed on: {string.Join(", ", failedDevices)}.";

            ControlMessage ack = new ControlMessage
            {
                SenderId = _gatewaySettings.GatewayId,
                UpdateAck = new UpdateAck
                {
                    FileName = pkg.FileName,
                    Success = overallSuccess,
                    Details = detailMsg
                }
            };
            await _requestStream.WriteAsync(ack);
        }

        /// <summary>
        /// Verifies the GPG signature of a package.
        /// </summary>
        private bool VerifySignature(string packagePath, string signaturePath)
        {
            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = "gpg",
                    Arguments = $"--homedir \"/home/patrick/.gnupg\" --verify \"{signaturePath}\" \"{packagePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                Process process = new Process { StartInfo = processStartInfo };
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    _logger.LogError($"Error verifying signature: {error}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error verifying signature: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Request version data from all devices and send it to the Cloud.
        /// </summary>
        private async Task HandleVersionDataRequest(VersionDataRequest req)
        {
            _logger.LogInformation("Received VersionDataRequest from Cloud.");

            List<Device> devices = _deviceRepository.GetAllDevices();
            Google.Protobuf.Collections.RepeatedField<DeviceVersions> devVersList = new Google.Protobuf.Collections.RepeatedField<DeviceVersions>();

            foreach (Device dev in devices)
            {
                if (dev.UUID == null)
                {
                    continue;
                }
                FullVersionInfo info = await _deviceCommunicationService.RequestVersionInfoAsync(dev);
                if (info == null) continue;

                DeviceVersions dv = new DeviceVersions
                {
                    DeviceUuid = dev.UUID
                };
                if (info.Current != null)
                {
                    dv.Current = new Protos.VersionInfo
                    {
                        Version = info.Current.Version,
                        InstalledAt = Timestamp.FromDateTime(info.Current.InstalledAt.ToUniversalTime())
                    };
                }
                foreach (VersionRecord av in info.Available)
                {
                    dv.Available.Add(new Protos.VersionInfo
                    {
                        Version = av.Version,
                        InstalledAt = Timestamp.FromDateTime(av.InstalledAt.ToUniversalTime())
                    });
                }
                devVersList.Add(dv);
            }

            ControlMessage resp = new ControlMessage
            {
                SenderId = _gatewaySettings.GatewayId,
                VersionDataResponse = new VersionDataResponse
                {
                    Success = true,
                    Message = $"Found version info for {devVersList.Count} devices"
                }
            };
            resp.VersionDataResponse.DeviceVersionsList.AddRange(devVersList);

            await _requestStream.WriteAsync(resp);
        }

        /// <summary>
        /// Called by DeviceCommunicationService when it has new monitoring data 
        /// it wants to push to the Cloud.
        /// </summary>
        public async Task SendMonitoringDataAsync(MonitoringDataMessage mon)
        {
            if (_requestStream == null)
            {
                _logger.LogWarning("Cannot send monitoring data, no active Cloud connection");
                return;
            }
            ControlMessage msg = new ControlMessage
            {
                SenderId = _gatewaySettings.GatewayId,
                MonitoringData = mon
            };
            await _requestStream.WriteAsync(msg);
            _logger.LogInformation("Sent MonitoringData with {0} items to Cloud", mon.AggregatedData.Count);
        }

        /// <summary>
        /// The device's license is invalid. We want to send a LicenseRequest to the Cloud (registration or renewal).
        /// We'll check if we haven't already requested it.
        /// </summary>
        public async Task SendLicenseRequestAsync(string deviceUuid, bool isRenewal)
        {
            if (_requestStream == null)
            {
                _logger.LogWarning("Cannot send license request, no active Cloud connection");
                return;
            }

            // Ensure we don't send if there's already a request in flight
            if (!_licenseRequestsInFlight.TryAdd(deviceUuid, isRenewal))
            {
                _logger.LogInformation("Already have a license request in flight for device {0}", deviceUuid);
                return;
            }

            LicenseRequest req = new LicenseRequest
            {
                DeviceUuid = deviceUuid,
                IsRenewal = isRenewal
            };
            ControlMessage msg = new ControlMessage
            {
                SenderId = _gatewaySettings.GatewayId,
                LicenseRequest = req
            };
            await _requestStream.WriteAsync(msg);
            _logger.LogInformation("Sent LicenseRequest for device {0}, isRenewal={1}", deviceUuid, isRenewal);
        }

        /// <summary>
        /// For sending an "AlertMessage" up to the Cloud (like critical logs or command status).
        /// </summary>
        public async Task SendAlertAsync(AlertMessage alert)
        {
            if (_requestStream == null)
            {
                _logger.LogWarning("Cannot send alert, no active Cloud connection");
                return;
            }
            ControlMessage msg = new ControlMessage
            {
                SenderId = _gatewaySettings.GatewayId,
                AlertMessage = alert
            };
            await _requestStream.WriteAsync(msg);
            _logger.LogInformation("Sent AlertMessage to Cloud: {0}", alert.Message);
        }

        /// <summary>
        /// To send AI recommendations to the Cloud.
        /// </summary>
        public async Task SendAiRecommendationsAsync(AiClusteringResult clusters, AiSchedulingResult schedule)
        {
            if (_requestStream == null)
            {
                _logger.LogWarning("No active Cloud connection, cannot send AI recommendations");
                return;
            }

            AIRecommendations recs = new AIRecommendations();

            // fill scheduling clusters
            if (schedule != null && schedule.Clusters != null)
            {
                foreach (AiScheduledCluster sc in schedule.Clusters)
                {
                    AIScheduledCluster asc = new AIScheduledCluster
                    {
                        ClusterId = sc.ClusterId,
                        UpdateTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(sc.UpdateTimeUtc.ToUniversalTime())
                    };
                    asc.DeviceUuids.AddRange(sc.DeviceUuids);
                    recs.Clusters.Add(asc);
                }
            }

            //  fill plot data
            if (clusters != null && clusters.PlotData != null)
            {
                foreach (AiPlotPoint p in clusters.PlotData)
                {
                    AIPlotPoint pp = new AIPlotPoint
                    {
                        DeviceUuid = p.DeviceUuid,
                        X = p.X,
                        Y = p.Y,
                        ClusterId = p.ClusterId
                    };
                    recs.PlotData.Add(pp);
                }
            }

            ControlMessage ctrlMsg = new ControlMessage
            {
                SenderId = _gatewaySettings.GatewayId,
                AiRecommendations = recs
            };

            await _requestStream.WriteAsync(ctrlMsg);
            _logger.LogInformation("Sent AIRecommendations to Cloud: {0} clusters, {1} plot points.",
                recs.Clusters.Count, recs.PlotData.Count);
        }


        /// <summary>
        /// Maps the Protobuf CommandType to the C# CommandType enum.
        /// </summary>
        /// <param name="commandType">The Protobuf CommandType.</param>
        /// <returns>The C# CommandType.</returns>
        private Enums.CommandType MapCommandType(Protos.CommandType commandType)
        {
            return commandType switch
            {
                Protos.CommandType.Rollback => Enums.CommandType.Rollback,
                _ => Enums.CommandType.Rollback, // default to unknown for now
            };
        }

        /// <summary>
        /// To handle a ScheduledUpdate message from the Cloud.
        /// </summary>
        private async Task HandleScheduledUpdate(ScheduledUpdate su)
        {
            _logger.LogInformation("Received ScheduledUpdate: ID={0}, startTime={1}, fileName={2}",
                su.ScheduleId, su.StartTime, su.FileName);

            string tempFilePath = Path.GetTempFileName();
            string signatureFilePath = tempFilePath + ".sig";

            await File.WriteAllBytesAsync(tempFilePath, su.PackageData.ToByteArray());
            await File.WriteAllBytesAsync(signatureFilePath, su.SignatureData.ToByteArray());

            bool verified = VerifySignature(tempFilePath, signatureFilePath);
            if (!verified)
            {
                _logger.LogError("Signature verification failed for scheduled update '{file}'", su.FileName);
                return;
            }

            _logger.LogInformation("Signature verification succeeded. Proceeding with scheduling.");

            DateTime startUtc = su.StartTime.ToDateTime();
            ScheduledUpdateEntry entry = new ScheduledUpdateEntry
            {
                ScheduleId = su.ScheduleId,
                DeviceUuids = su.DeviceUuids.ToList(),
                FileName = su.FileName,
                PackageData = su.PackageData.ToByteArray(),
                StartTimeUtc = startUtc
            };

            lock (_scheduledUpdates)
            {
                _scheduledUpdates[entry.ScheduleId] = entry;
            }

            // Acknowledge
            if (_requestStream != null)
            {
                CommandResponse resp = new CommandResponse
                {
                    CommandId = su.ScheduleId,
                    Success = true,
                    Details = $"Scheduled update stored for {startUtc:o}"
                };
                ControlMessage msg = new ControlMessage
                {
                    SenderId = _gatewaySettings.GatewayId,
                    CommandResponse = resp
                };
                await _requestStream.WriteAsync(msg);
            }
        }

        /// <summary>
        /// To send the DeviceStatus to the Cloud.
        /// Note: This is called by DevicePingService when a device's status changes.
        /// </summary>
        public async Task SendDeviceStatusAsync(DeviceStatus statusMsg)
        {
            if (_requestStream == null)
            {
                _logger.LogWarning("No active Cloud connection to send device status");
                return;
            }

            ControlMessage msg = new ControlMessage
            {
                SenderId = _gatewaySettings.GatewayId,
                DeviceStatus = statusMsg
            };

            await _requestStream.WriteAsync(msg);
            _logger.LogInformation("Sent DeviceStatus for {0} => isOnline={1}",
                statusMsg.DeviceUuid, statusMsg.IsOnline);
        }
    }
}
