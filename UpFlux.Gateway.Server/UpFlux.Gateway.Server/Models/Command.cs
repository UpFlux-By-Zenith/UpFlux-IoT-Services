using System;
using UpFlux.Gateway.Server.Enums;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents a command to be executed on devices.
    /// </summary>
    public class Command
    {
        /// <summary>
        /// Gets or sets the unique identifier of the command.
        /// </summary>
        public string CommandId { get; set; }

        /// <summary>
        /// Gets or sets the type of the command.
        /// </summary>
        public CommandType CommandType { get; set; }

        /// <summary>
        /// Gets or sets the target devices for the command.
        /// </summary>
        public string[] TargetDevices { get; set; }

        /// <summary>
        /// Gets or sets any parameters associated with the command.
        /// </summary>
        public string Parameters { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) when the command was received.
        /// </summary>
        public DateTime ReceivedAt { get; set; }
    }
}
