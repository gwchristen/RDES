using System;

namespace RDES.App.Models
{
    public class AppConfig
    {
        public string DatabasePath { get; set; } = string.Empty;
        public int BusyTimeoutMs { get; set; } = 10000;
        public bool AutoUppercaseSerials { get; set; } = true;
        public string Theme { get; set; } = "System";
        public bool IsDarkMode { get; set; } = false;
        public int PageSize { get; set; } = 500;
        public string LastUsedSheetName { get; set; } = "RMA Entry";
        public string AdminPin { get; set; } = "1234";
    }
}
