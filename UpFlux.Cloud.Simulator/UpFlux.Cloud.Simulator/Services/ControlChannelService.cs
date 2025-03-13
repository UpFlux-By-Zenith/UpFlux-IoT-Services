using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using UpFlux.Cloud.Simulator.Protos;

namespace UpFlux.Cloud.Simulator
{
    /// <summary>
    /// Unified gRPC service that handles all operations (License, Commands, Logs, Monitoring, Alerts)
    /// via a single persistent streaming method (OpenControlChannel).
    /// </summary>
    public class ControlChannelService : ControlChannel.ControlChannelBase
    {
        private readonly ILogger<ControlChannelService> _logger;

        // A dictionary of "GatewayID" => the active IServerStreamWriter
        private readonly ConcurrentDictionary<string, IServerStreamWriter<ControlMessage>> _connectedGateways
            = new ConcurrentDictionary<string, IServerStreamWriter<ControlMessage>>();

        public ControlChannelService(ILogger<ControlChannelService> logger)
        {
            _logger = logger;
        }

        public override async Task OpenControlChannel(
            IAsyncStreamReader<ControlMessage> requestStream,
            IServerStreamWriter<ControlMessage> responseStream,
            ServerCallContext context)
        {
            string gatewayId = "UNKNOWN";
            try
            {
                // Expect the first message to identify the gateway
                if (!await requestStream.MoveNext())
                {
                    _logger.LogWarning("No initial message from gateway; closing channel.");
                    return;
                }

                ControlMessage firstMsg = requestStream.Current;
                gatewayId = firstMsg.SenderId ?? "UNKNOWN";
                _connectedGateways[gatewayId] = responseStream;

                _logger.LogInformation("Gateway [{0}] connected to ControlChannel.", gatewayId);

                // Optionally handle the first message if it has a payload
                await HandleIncomingMessage(gatewayId, firstMsg);

                // Continue reading messages until the gateway disconnects
                while (await requestStream.MoveNext())
                {
                    await HandleIncomingMessage(gatewayId, requestStream.Current);
                }

                _logger.LogInformation("Gateway [{0}] disconnected.", gatewayId);
            }
            finally
            {
                _connectedGateways.TryRemove(gatewayId, out _);
            }
        }

        private async Task HandleIncomingMessage(string gatewayId, ControlMessage msg)
        {
            switch (msg.PayloadCase)
            {
                case ControlMessage.PayloadOneofCase.LicenseRequest:
                    await HandleLicenseRequest(gatewayId, msg.LicenseRequest);
                    break;
                case ControlMessage.PayloadOneofCase.LogUpload:
                    await HandleLogUpload(gatewayId, msg.LogUpload);
                    break;
                case ControlMessage.PayloadOneofCase.MonitoringData:
                    HandleMonitoringData(gatewayId, msg.MonitoringData);
                    break;
                case ControlMessage.PayloadOneofCase.AlertMessage:
                    await HandleAlertMessage(gatewayId, msg.AlertMessage);
                    break;
                case ControlMessage.PayloadOneofCase.CommandResponse:
                    _logger.LogInformation("Gateway [{0}] responded to command: {1}", gatewayId, msg.CommandResponse.CommandId);
                    break;
                case ControlMessage.PayloadOneofCase.UpdateAck:
                    _logger.LogInformation("Gateway [{0}] acknowledged update: {1}, success={2}",
                        gatewayId, msg.UpdateAck.FileName, msg.UpdateAck.Success);
                    break;
                case ControlMessage.PayloadOneofCase.LogResponse:
                    _logger.LogInformation("Gateway [{0}] responded to log request => success={1}, msg={2}",
                        gatewayId, msg.LogResponse.Success, msg.LogResponse.Message);
                    break;
                case ControlMessage.PayloadOneofCase.VersionDataResponse:
                    HandleVersionDataResponse(gatewayId, msg.VersionDataResponse);
                    break;
                case ControlMessage.PayloadOneofCase.AiRecommendations:
                    HandleAiRecommendations(gatewayId, msg.AiRecommendations);
                    break;
                case ControlMessage.PayloadOneofCase.DeviceStatus:
                    HandleDeviceStatus(gatewayId, msg.DeviceStatus);
                    break;
                default:
                    _logger.LogWarning("Received unknown message from [{0}] => {1}", gatewayId, msg.PayloadCase);
                    break;
            }
        }

        // ---------- EXACT license logic (with console prompt) ----------
        private async Task HandleLicenseRequest(string gatewayId, LicenseRequest req)
        {
            _logger.LogInformation("Received LicenseRequest: device={0}, isRenewal={1}", req.DeviceUuid, req.IsRenewal);

            ConsoleSync.WriteLine($"\n[LicenseService] {(req.IsRenewal ? "RenewLicense" : "RegisterDevice")} for {req.DeviceUuid}. Approve? (y/n)");
            char key = ConsoleSync.ReadKey();
            ConsoleSync.WriteLine("");
            bool approved = (key == 'y' || key == 'Y');

            DateTime expiration = DateTime.UtcNow.AddMonths(req.IsRenewal ? 2 : 1);
            string signature = req.IsRenewal ? "TestBase64SignatureIfAny" : "TestBase64Signature";
            string xmlLicense = $@"
                <Licence>
                  <ExpirationDate>{expiration:o}</ExpirationDate>
                  <MachineID>{req.DeviceUuid}</MachineID>
                  <Signature>{signature}</Signature>
                </Licence>";

            LicenseResponse licenseResp = new LicenseResponse
            {
                DeviceUuid = req.DeviceUuid,
                Approved = approved,
                License = approved ? xmlLicense : "",
                ExpirationDate = Timestamp.FromDateTime(expiration)
            };

            if (_connectedGateways.TryGetValue(gatewayId, out IServerStreamWriter<ControlMessage>? writer))
            {
                ControlMessage outMsg = new ControlMessage
                {
                    SenderId = "CloudSim",
                    LicenseResponse = licenseResp
                };
                await writer.WriteAsync(outMsg);
                _logger.LogInformation("LicenseResponse sent to [{0}] => device={1}, approved={2}",
                    gatewayId, req.DeviceUuid, approved);
            }
        }

        // ---------- EXACT log saving logic ----------
        private async Task HandleLogUpload(string gatewayId, LogUpload upload)
        {
            _logger.LogInformation("Received LogUpload from device={0} at gateway=[{1}], file={2}, size={3} bytes",
                upload.DeviceUuid, gatewayId, upload.FileName, upload.Data.Length);

            Directory.CreateDirectory("CloudLogs");
            string path = Path.Combine("CloudLogs", upload.FileName);
            await File.WriteAllBytesAsync(path, upload.Data.ToByteArray());

            _logger.LogInformation("Logs saved to: {0}", path);
        }

        // ---------- EXACT monitoring logic ----------
        private void HandleMonitoringData(string gatewayId, MonitoringDataMessage mon)
        {
            foreach (AggregatedData? agg in mon.AggregatedData)
            {
                _logger.LogInformation("Monitoring from dev={0} (gw={1}): CPU={2}%, MEM={3}%",
                    agg.Uuid, gatewayId, agg.Metrics.CpuUsage, agg.Metrics.MemoryUsage);
            }
        }

        // ---------- EXACT alert logic ----------
        private async Task HandleAlertMessage(string gatewayId, AlertMessage alert)
        {
            _logger.LogInformation("ALERT from gw={0}, dev={1}, level={2}, msg={3}",
                gatewayId, alert.Source, alert.Level, alert.Message);

            // Send an alertResponse back
            if (_connectedGateways.TryGetValue(gatewayId, out IServerStreamWriter<ControlMessage>? writer))
            {
                ControlMessage responseMsg = new ControlMessage
                {
                    SenderId = "CloudSim",
                    AlertResponse = new AlertResponseMessage
                    {
                        Success = true,
                        Message = "CloudSim: alert received"
                    }
                };
                await writer.WriteAsync(responseMsg);
            }
        }

        // ---------- EXACT device status logic ----------
        private void HandleDeviceStatus(string gatewayId, DeviceStatus status)
        {
            _logger.LogInformation(
                "DeviceStatus from Gateway [{0}]: device={1}, isOnline={2}, changedAt={3}",
                gatewayId, status.DeviceUuid, status.IsOnline, status.LastSeen
            );
        }

        // ---------- EXACT version data logic ----------
        private void HandleVersionDataResponse(string gatewayId, VersionDataResponse resp)
        {
            if (!resp.Success)
            {
                _logger.LogWarning("Gateway [{0}] reported version data request failed: {1}", gatewayId, resp.Message);
            }
            else
            {
                _logger.LogInformation("VersionDataResponse from [{0}]: {1}", gatewayId, resp.Message);
                foreach (DeviceVersions? dv in resp.DeviceVersionsList)
                {
                    _logger.LogInformation(" Device={0}", dv.DeviceUuid);
                    if (dv.Current != null)
                    {
                        DateTime installed = dv.Current.InstalledAt.ToDateTime();
                        _logger.LogInformation("  CURRENT => Version={0}, InstalledAt={1}", dv.Current.Version, installed);
                    }
                    else
                    {
                        _logger.LogInformation("  CURRENT => (none)");
                    }

                    if (dv.Available.Count > 0)
                    {
                        _logger.LogInformation("  AVAILABLE:");
                        foreach (Protos.VersionInfo? av in dv.Available)
                        {
                            _logger.LogInformation("    - Version={0}, InstalledAt={1}",
                                av.Version, av.InstalledAt.ToDateTime());
                        }
                    }
                    else
                    {
                        _logger.LogInformation("  AVAILABLE => (none)");
                    }
                }
            }
        }

        // ---------- EXACT AI recommendations logic ----------
        private void HandleAiRecommendations(string gatewayId, AIRecommendations aiRec)
        {
            _logger.LogInformation("AI Recommendations from [{0}]:", gatewayId);

            foreach (AIScheduledCluster? cluster in aiRec.Clusters)
            {
                _logger.LogInformation(" Cluster={0}, updated={1}", cluster.ClusterId, cluster.UpdateTime.ToDateTime());
                _logger.LogInformation("  Devices: {0}", string.Join(", ", cluster.DeviceUuids));
            }

            foreach (AIPlotPoint? plot in aiRec.PlotData)
            {
                _logger.LogInformation(" Plot: dev={0}, x={1}, y={2}, cluster={3}",
                    plot.DeviceUuid, plot.X, plot.Y, plot.ClusterId);
            }
        }

        // ---------- PUBLIC METHODS to push messages from the console menu ----------

        /// <summary>
        /// Sends a command request (e.g. ROLLBACK) to a connected gateway.
        /// </summary>
        public async Task SendCommandToGatewayAsync(string gatewayId,
                                                    string commandId,
                                                    CommandType cmdType,
                                                    string parameters,
                                                    params string[] targetDevices)
        {
            if (!_connectedGateways.TryGetValue(gatewayId, out IServerStreamWriter<ControlMessage>? writer))
            {
                _logger.LogWarning("Gateway [{0}] is not connected.", gatewayId);
                return;
            }

            CommandRequest cmdReq = new CommandRequest
            {
                CommandId = commandId,
                CommandType = cmdType,
                Parameters = parameters
            };
            cmdReq.TargetDevices.AddRange(targetDevices);

            ControlMessage msg = new ControlMessage
            {
                SenderId = "CloudSim",
                CommandRequest = cmdReq
            };
            await writer.WriteAsync(msg);

            _logger.LogInformation("CommandRequest sent to [{0}]: id={1}, type={2}, deviceCount={3}",
                gatewayId, commandId, cmdType, targetDevices.Length);
        }

        /// <summary>
        /// Requests logs from specified devices on the connected gateway.
        /// </summary>
        public async Task SendLogRequestAsync(string gatewayId, string[] deviceUuids)
        {
            if (!_connectedGateways.TryGetValue(gatewayId, out IServerStreamWriter<ControlMessage>? writer))
            {
                _logger.LogWarning("Gateway [{0}] is not connected.", gatewayId);
                return;
            }

            LogRequestMessage req = new LogRequestMessage();
            req.DeviceUuids.AddRange(deviceUuids);

            ControlMessage msg = new ControlMessage
            {
                SenderId = "CloudSim",
                LogRequest = req
            };
            await writer.WriteAsync(msg);

            _logger.LogInformation("LogRequest sent to [{0}] for {1} device(s).", gatewayId, deviceUuids.Length);
        }

        /// <summary>
        /// Sends an update package to the gateway, which should forward/install it on the specified devices.
        /// </summary>
        public async Task SendUpdatePackageAsync(string gatewayId, string fileName, byte[] packageData, byte[] signatureData, string[] targetDevices)
        {
            if (!_connectedGateways.TryGetValue(gatewayId, out IServerStreamWriter<ControlMessage>? writer))
            {
                _logger.LogWarning("Gateway [{0}] is not connected.", gatewayId);
                return;
            }

            UpdatePackage update = new UpdatePackage
            {
                FileName = fileName,
                PackageData = Google.Protobuf.ByteString.CopyFrom(packageData),
                SignatureData = Google.Protobuf.ByteString.CopyFrom(signatureData)
            };
            update.TargetDevices.AddRange(targetDevices);

            ControlMessage msg = new ControlMessage
            {
                SenderId = "CloudSim",
                UpdatePackage = update
            };
            await writer.WriteAsync(msg);

            _logger.LogInformation("UpdatePackage [{0}] of size {1} bytes sent to [{2}], for {3} devices.",
                fileName, packageData.Length, gatewayId, targetDevices.Length);
        }

        /// <summary>
        /// Sends a request for version data; the gateway should respond with a VersionDataResponse.
        /// </summary>
        public async Task SendVersionDataRequestAsync(string gatewayId)
        {
            if (!_connectedGateways.TryGetValue(gatewayId, out IServerStreamWriter<ControlMessage>? writer))
            {
                _logger.LogWarning("Gateway [{0}] is not connected.", gatewayId);
                return;
            }

            ControlMessage msg = new ControlMessage
            {
                SenderId = "CloudSim",
                VersionDataRequest = new VersionDataRequest()
            };
            await writer.WriteAsync(msg);

            _logger.LogInformation("VersionDataRequest sent to gateway [{0}].", gatewayId);
        }

        /// <summary>
        /// Sends a ScheduledUpdate to the gateway, which should install the package on the specified devices.
        /// </summary>
        /// <param name="gatewayId">The gateway to send the update to</param>
        /// <param name="scheduleId">The unique ID for this scheduled update</param>
        /// <param name="deviceUuids">The devices to target</param>
        /// <param name="fileName">The name of the update package</param>
        /// <param name="packageData">The binary data of the update package</param>
        /// <param name="startTimeUtc">The start time for the update</param>
        /// <returns>Returns the task for the async operation</returns>
        public async Task SendScheduledUpdateAsync(
            string gatewayId,
            string scheduleId,
            string[] deviceUuids,
            string fileName,
            byte[] packageData,
            DateTime startTimeUtc
        )
        {
            if (!_connectedGateways.TryGetValue(gatewayId, out IServerStreamWriter<ControlMessage>? writer))
            {
                _logger.LogWarning("Gateway [{0}] is not connected.", gatewayId);
                return;
            }

            // build ScheduledUpdate
            ScheduledUpdate su = new ScheduledUpdate
            {
                ScheduleId = scheduleId,
                FileName = fileName,
                PackageData = Google.Protobuf.ByteString.CopyFrom(packageData),
                StartTime = Timestamp.FromDateTime(startTimeUtc.ToUniversalTime())
            };
            su.DeviceUuids.AddRange(deviceUuids);

            ControlMessage msg = new ControlMessage
            {
                SenderId = "CloudSim",
                ScheduledUpdate = su
            };

            await writer.WriteAsync(msg);
            _logger.LogInformation("ScheduledUpdate {0} sent to gateway [{1}], devices={2}, start={3}",
                scheduleId, gatewayId, string.Join(",", deviceUuids), startTimeUtc.ToString("o"));
        }

    }
}
