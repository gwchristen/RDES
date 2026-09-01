using System;

namespace RDES.App.Models
{
    public class StatisticItem
    {
        public string Key { get; set; } = string.Empty;
        public string SubKey { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
        public string FormattedPercentage => $"{Percentage:F1}%";
    }

    public class StatisticsSummary
    {
        public int TotalCount { get; set; }
        public int PendingCount { get; set; }
        public int SubmittedCount { get; set; }
        public int UniqueSerialsCount { get; set; }
        public int UniqueUsersCount { get; set; }
        public int UniqueOpCosCount { get; set; }
        public string TopDefect { get; set; } = "N/A";
        public string TopDeviceCode { get; set; } = "N/A";
        public string TopOpCo { get; set; } = "N/A";
    }
}
