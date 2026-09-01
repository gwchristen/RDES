using System.Collections.Generic;

namespace RDES.App.Models
{
    public class ImportResult
    {
        public int TotalRead { get; set; }
        public int InsertedCount { get; set; }
        public int SkippedDuplicates { get; set; }
        public int UpdatedCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public bool Success => Errors.Count == 0;
    }
}
