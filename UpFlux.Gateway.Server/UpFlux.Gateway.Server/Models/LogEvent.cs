using System;

namespace UpFlux.Gateway.Server.Models
{
    /// <summary>
    /// Represents a log event.
    /// </summary>
    public class LogEvent
    {
        /// <summary>
        /// Gets or sets the timestamp of the log event.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the log level (e.g., "Error", "Fatal").
        /// </summary>
        public string Level { get; set; }

        /// <summary>
        /// Gets or sets the log message.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the exception associated with the log event, if any.
        /// </summary>
        public Exception Exception { get; set; }
    }
}
