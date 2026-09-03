using System;

namespace RDES.App.Models
{
    public class ModemIncidentLog
    {
        public long Id { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Severity { get; set; } = "Info"; // Info, Warning, Error, Critical
        public string EventType { get; set; } = "General"; // Disconnect, Reconnect, CommandTimeout, MaxRetriesExceeded, SoakCheckFailed, BatchError
        public string Message { get; set; } = string.Empty;
        public string PortName { get; set; } = string.Empty;
        public string ExceptionDetails { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = string.Empty;
    }
}
