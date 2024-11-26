using Serilog.Core;
using Serilog.Events;
using System.Threading.Tasks;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Services;

namespace UpFlux.Gateway.Server.Utilities
{
    /// <summary>
    /// A Serilog sink that captures critical logs and invokes the AlertingService.
    /// </summary>
    public class SerilogAlertingSink : ILogEventSink
    {
        private readonly IFormatProvider _formatProvider;
        private readonly AlertingService _alertingService;

        public SerilogAlertingSink(IFormatProvider formatProvider, AlertingService alertingService)
        {
            _formatProvider = formatProvider;
            _alertingService = alertingService;
        }

        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            // Check if the log level is Error or Fatal
            if (logEvent.Level == LogEventLevel.Error || logEvent.Level == LogEventLevel.Fatal)
            {
                string message = logEvent.RenderMessage(_formatProvider);

                Models.LogEvent alertLogEvent = new Models.LogEvent
                {
                    Timestamp = logEvent.Timestamp,
                    Level = logEvent.Level.ToString(),
                    Message = message,
                    Exception = logEvent.Exception
                };

                // Process the critical log asynchronously
                Task.Run(() => _alertingService.ProcessCriticalLogAsync(alertLogEvent));
            }
        }
    }
}
