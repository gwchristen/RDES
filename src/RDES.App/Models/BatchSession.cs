using System;

namespace RDES.App.Models
{
    public class BatchSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending"; // Pending, InProgress, Paused, Completed, Failed
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int CurrentIndex { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }
    }

    public class BatchSessionItem
    {
        public long Id { get; set; }
        public string BatchSessionId { get; set; } = string.Empty;
        public int ItemIndex { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending"; // Pending, InProgress, Success, Failed
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime? ProcessedAt { get; set; }
        public int RetryCount { get; set; }
    }
}
