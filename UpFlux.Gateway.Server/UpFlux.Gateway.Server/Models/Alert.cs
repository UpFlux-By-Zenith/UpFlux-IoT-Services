using System;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents an alert generated from a critical log event.
    /// </summary>
    public class Alert
    {
        /// <summary>
        /// Gets or sets the timestamp of the alert.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the severity level of the alert.
        /// </summary>
        public string Level { get; set; }

        /// <summary>
        /// Gets or sets the alert message.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the exception details, if any.
        /// </summary>
        public string Exception { get; set; }

        /// <summary>
        /// Gets or sets the source of the alert ("GatewayServer").
        /// </summary>
        public string Source { get; set; }
    }
}
