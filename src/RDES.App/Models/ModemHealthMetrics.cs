using System;

namespace RDES.App.Models
{
    public class ModemHealthMetrics
    {
        public ModemState State { get; set; } = ModemState.Disconnected;
        public int TotalRetries { get; set; }
        public int TotalDisconnects { get; set; }
        public int TotalRecoveries { get; set; }
        public double FailuresPerHour { get; set; }
        public double UptimePercentage { get; set; } = 100.0;
        public DateTime? LastConnectedAt { get; set; }
        public DateTime? LastDisconnectedAt { get; set; }
        public int TotalCommandsExecuted { get; set; }
        public int TotalCommandFailures { get; set; }
        public string ActivePortName { get; set; } = string.Empty;
        public bool IsSoakTesting { get; set; }
    }
}
