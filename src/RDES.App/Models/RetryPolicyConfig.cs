using System;

namespace RDES.App.Models
{
    public class RetryPolicyConfig
    {
        public int BaseDelayMs { get; set; } = 500;
        public int MaxDelayMs { get; set; } = 10000;
        public int MaxRetries { get; set; } = 5;
        public double JitterFactor { get; set; } = 0.2;
        public int CommandTimeoutMs { get; set; } = 3000;
    }
}
