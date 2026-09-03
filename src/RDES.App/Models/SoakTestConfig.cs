using System;

namespace RDES.App.Models
{
    public class SoakTestConfig
    {
        public double TargetDurationHours { get; set; } = 8.0;
        public int HealthCheckIntervalSeconds { get; set; } = 15;
        public bool StopOnFailure { get; set; } = false;
    }

    public class SoakTestStatus
    {
        public bool IsRunning { get; set; }
        public DateTime? StartTime { get; set; }
        public double TargetDurationHours { get; set; } = 8.0;
        public TimeSpan ElapsedTime { get; set; }
        public int TotalChecks { get; set; }
        public int PassedChecks { get; set; }
        public int FailedChecks { get; set; }
        public int RecoveryCount { get; set; }
        public string StatusMessage { get; set; } = "Not Started";
    }
}
