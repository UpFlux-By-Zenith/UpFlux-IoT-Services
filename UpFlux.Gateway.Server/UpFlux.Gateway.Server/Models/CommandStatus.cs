using System.Collections.Generic;
using UpFlux.Gateway.Server.Enums;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents the status of a command execution.
    /// </summary>
    public class CommandStatus
    {
        /// <summary>
        /// Gets or sets the command identifier.
        /// </summary>
        public string CommandId { get; set; }

        /// <summary>
        /// Gets or sets the type of the command.
        /// </summary>
        public CommandType CommandType { get; set; }

        /// <summary>
        /// Gets or sets the list of target device UUIDs.
        /// </summary>
        public string[] TargetDevices { get; set; }

        /// <summary>
        /// Gets or sets the set of devices that have successfully executed the command.
        /// </summary>
        public HashSet<string> DevicesSucceeded { get; set; }

        /// <summary>
        /// Gets or sets the set of devices that failed to execute the command.
        /// </summary>
        public HashSet<string> DevicesFailed { get; set; }

        /// <summary>
        /// Gets or sets the set of devices that are pending command execution.
        /// </summary>
        public HashSet<string> DevicesPending { get; set; }
    }
}
