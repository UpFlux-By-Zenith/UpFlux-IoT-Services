using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Enums;
using UpFlux.Gateway.Server.Protos;

namespace UpFlux.Gateway.Server.Services
{
    /// <summary>
    /// gRPC service implementation for receiving commands from the cloud.
    /// </summary>
    public class CommandServiceGrpc : CommandService.CommandServiceBase
    {
        private readonly ILogger<CommandServiceGrpc> _logger;
        private readonly CommandExecutionService _commandExecutionService;

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandServiceGrpc"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="commandExecutionService">Command execution service.</param>
        public CommandServiceGrpc(
            ILogger<CommandServiceGrpc> logger,
            CommandExecutionService commandExecutionService)
        {
            _logger = logger;
            _commandExecutionService = commandExecutionService;
        }

        /// <inheritdoc/>
        public override async Task<Google.Protobuf.WellKnownTypes.Empty> SendCommand(CommandRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Received command {commandId} of type {commandType} from cloud.", request.CommandId, request.CommandType);

            try
            {
                // Create Command model
                Command command = new Command
                {
                    CommandId = request.CommandId,
                    CommandType = MapCommandType(request.CommandType),
                    TargetDevices = request.TargetDevices.ToArray(),
                    Parameters = request.Parameters,
                    ReceivedAt = DateTime.UtcNow
                };

                // Handle the command
                await _commandExecutionService.HandleCommandAsync(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing command {commandId}", request.CommandId);
                throw new RpcException(new Status(StatusCode.Internal, "Failed to process command."));
            }

            return new Google.Protobuf.WellKnownTypes.Empty();
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
    }
}
