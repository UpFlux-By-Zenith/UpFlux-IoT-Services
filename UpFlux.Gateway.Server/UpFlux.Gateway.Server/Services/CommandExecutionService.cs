using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpFlux.Gateway.Server.Enums;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Services;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// Service responsible for executing commands received from the cloud, such as rollbacks.
    /// </summary>
    public class CommandExecutionService
    {
        private readonly ILogger<CommandExecutionService> _logger;
        private readonly DeviceCommunicationService _deviceCommunicationService;
        private readonly AlertingService _alertingService;
        private readonly ConcurrentDictionary<string, CommandStatus> _commandStatuses;
        private readonly GatewaySettings _gatewaySettings;

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandExecutionService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="deviceCommunicationService">Service for communication with devices.</param>
        public CommandExecutionService(
            ILogger<CommandExecutionService> logger,
            DeviceCommunicationService deviceCommunicationService,
            AlertingService alertingService,
            IOptions<GatewaySettings> gatewaySettings)
        {
            _logger = logger;
            _deviceCommunicationService = deviceCommunicationService;
            _commandStatuses = new ConcurrentDictionary<string, CommandStatus>();
            _alertingService = alertingService;
            _gatewaySettings = gatewaySettings.Value;
        }

        /// <summary>
        /// Handles a new command received from the cloud.
        /// </summary>
        /// <param name="command">The command to process.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task<CommandStatus> HandleCommandAsync(Command command)
        {
            _logger.LogInformation("Processing command {commandId} of type {commandType}", command.CommandId, command.CommandType);

            CommandStatus commandStatus = null;

            try
            {
                // Validate the command
                if (!ValidateCommand(command))
                {
                    _logger.LogWarning("Command {commandId} failed validation.", command.CommandId);
                    return null;
                }

                commandStatus = new CommandStatus
                {
                    CommandId = command.CommandId,
                    CommandType = command.CommandType,
                    TargetDevices = command.TargetDevices,
                    DevicesPending = new HashSet<string>(command.TargetDevices),
                    DevicesSucceeded = new HashSet<string>(),
                    DevicesFailed = new HashSet<string>()
                };

                _commandStatuses[command.CommandId] = commandStatus;

                // Execute the command on devices
                await ExecuteCommandAsync(command, commandStatus);

                // Report statuses back to the cloud
                await ReportCommandStatusAsync(commandStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing command {commandId}", command.CommandId);
            }

            return commandStatus;
        }

        /// <summary>
        /// Validates the command to ensure it is authorized and well-formed.
        /// </summary>
        /// <param name="command">The command to validate.</param>
        /// <returns>True if the command is valid; otherwise, false.</returns>
        private bool ValidateCommand(Command command)
        {
            // Will Implement a logic later on to validate the command if needed
            return true;
        }

        /// <summary>
        /// Executes the command on the target devices.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        /// <param name="commandStatus">The command status to track progress.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ExecuteCommandAsync(Command command, CommandStatus commandStatus)
        {
            _logger.LogInformation("Executing command {commandId} on devices.", command.CommandId);

            List<Task> tasks = new List<Task>();

            foreach (string deviceUuid in command.TargetDevices)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("Sending command {commandId} to device {deviceUuid}", command.CommandId, deviceUuid);

                        bool success = false;

                        // Handle different command types
                        switch (command.CommandType)
                        {
                            case Enums.CommandType.Rollback:
                                success = await _deviceCommunicationService.SendRollbackCommandAsync(deviceUuid, command.Parameters);
                                break;
                            // Add cases for other command types when needed
                            default:
                                _logger.LogWarning("Unknown command type {commandType} for command {commandId}", command.CommandType, command.CommandId);
                                break;
                        }

                        if (success)
                        {
                            _logger.LogInformation("Command {commandId} successfully executed on device {deviceUuid}", command.CommandId, deviceUuid);
                            commandStatus.DevicesSucceeded.Add(deviceUuid);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to execute command {commandId} on device {deviceUuid}", command.CommandId, deviceUuid);
                            commandStatus.DevicesFailed.Add(deviceUuid);
                        }

                        commandStatus.DevicesPending.Remove(deviceUuid);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error executing command {commandId} on device {deviceUuid}", command.CommandId, deviceUuid);
                        commandStatus.DevicesFailed.Add(deviceUuid);
                        commandStatus.DevicesPending.Remove(deviceUuid);
                    }
                }));
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);

            _logger.LogInformation("Command {commandId} execution completed.", command.CommandId);
        }

        /// <summary>
        /// Reports the command execution status back to the cloud.
        /// </summary>
        /// <param name="commandStatus">The command status to report.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ReportCommandStatusAsync(CommandStatus commandStatus)
        {
            _logger.LogInformation("Reporting status of command {commandId} back to the cloud.", commandStatus.CommandId);

            // Create an alert or status message
            Alert alert = new Alert
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = "Information",
                Message = $"Command {commandStatus.CommandId} of type {commandStatus.CommandType} executed. " +
                          $"Succeeded on devices: {string.Join(", ", commandStatus.DevicesSucceeded)}. " +
                          $"Failed on devices: {string.Join(", ", commandStatus.DevicesFailed)}.",
                Source = _gatewaySettings.GatewayId
            };

            // Send alert to the cloud
            await _alertingService.ProcessDeviceLogAsync(alert);
        }

        /// <summary>
        /// Gets the command status for a given command ID.
        /// </summary>
        /// <param name="commandId">The command identifier.</param>
        /// <returns>The command status.</returns>
        public CommandStatus GetCommandStatus(string commandId)
        {
            _commandStatuses.TryGetValue(commandId, out CommandStatus? status);
            return status;
        }
    }
}
