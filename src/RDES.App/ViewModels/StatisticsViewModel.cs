using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RDES.App.Models;
using RDES.App.Services;

namespace RDES.App.ViewModels
{
    public partial class StatisticsViewModel : ObservableObject
    {
        private readonly DeviceRepository _repository;
        private readonly ExcelService _excelService;
        private readonly ConfigService _configService;

        // Filter Properties
        [ObservableProperty]
        private DateTime? _fromDate = null;

        [ObservableProperty]
        private DateTime? _toDate = null;

        [ObservableProperty]
        private string _selectedOpCo = "All";

        [ObservableProperty]
        private string _selectedUser = "All";

        [ObservableProperty]
        private string _selectedDefect = "All";

        [ObservableProperty]
        private string _selectedDeviceCode = "All";

        [ObservableProperty]
        private string _selectedStatus = "All";

        [ObservableProperty]
        private string _selectedGroupBy = "OpCo";

        [ObservableProperty]
        private bool _isBusy = false;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        // Summary KPI Properties
        [ObservableProperty]
        private int _totalRecords = 0;

        [ObservableProperty]
        private int _pendingRecords = 0;

        [ObservableProperty]
        private int _submittedRecords = 0;

        [ObservableProperty]
        private int _uniqueSerials = 0;

        [ObservableProperty]
        private int _activeOperators = 0;

        [ObservableProperty]
        private string _topDefect = "N/A";

        [ObservableProperty]
        private string _topDeviceCode = "N/A";

        [ObservableProperty]
        private string _topOpCo = "N/A";

        // Filter Collections
        public ObservableCollection<string> OpCoList { get; } = new() { "All" };
        public ObservableCollection<string> UserList { get; } = new() { "All" };
        public ObservableCollection<string> DefectList { get; } = new() { "All" };
        public ObservableCollection<string> DeviceCodeList { get; } = new() { "All" };
        public ObservableCollection<string> StatusList { get; } = new() { "All", "Pending", "Submitted" };

        public ObservableCollection<string> GroupByOptions { get; } = new()
        {
            "OpCo",
            "Device Code",
            "Defect / Issue",
            "Operator / User",
            "Status",
            "Daily Scan Volume (Date)",
            "OpCo + Defect",
            "Operator + OpCo",
            "Device Code + Defect",
            "OpCo + Device Code",
            "Manufacturer Code"
        };

        // Data Breakdown Tables
        public ObservableCollection<StatisticItem> GroupedResults { get; } = new();
        public ObservableCollection<StatisticItem> OpCoBreakdown { get; } = new();
        public ObservableCollection<StatisticItem> DefectBreakdown { get; } = new();
        public ObservableCollection<StatisticItem> UserBreakdown { get; } = new();
        public ObservableCollection<StatisticItem> DeviceCodeBreakdown { get; } = new();

        public StatisticsViewModel(DeviceRepository repository, ExcelService excelService, ConfigService configService)
        {
            _repository = repository;
            _excelService = excelService;
            _configService = configService;
        }

        public async Task InitializeAsync()
        {
            await LoadFilterOptionsAsync();
            await RefreshStatisticsAsync();
        }

        public async Task LoadFilterOptionsAsync()
        {
            try
            {
                // OpCos
                var opcos = await _repository.GetOpCoOptionsAsync();
                OpCoList.Clear();
                OpCoList.Add("All");
                foreach (var o in opcos) OpCoList.Add(o.Name);

                // Users
                var users = await _repository.GetDistinctUsersAsync();
                UserList.Clear();
                UserList.Add("All");
                foreach (var u in users) UserList.Add(u);

                // Defects
                var defects = await _repository.GetDefectOptionsAsync();
                DefectList.Clear();
                DefectList.Add("All");
                foreach (var d in defects) DefectList.Add(d.Name);

                // Device Codes
                var codes = await _repository.GetDistinctDeviceCodesAsync();
                DeviceCodeList.Clear();
                DeviceCodeList.Add("All");
                foreach (var c in codes) DeviceCodeList.Add(c);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load filter options: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task RefreshStatisticsAsync()
        {
            IsBusy = true;
            try
            {
                // 1. Fetch Summary KPIs
                var summary = await _repository.GetFilteredSummaryMetricsAsync(
                    FromDate, ToDate, SelectedOpCo, SelectedUser, SelectedDefect, SelectedDeviceCode, SelectedStatus
                );

                TotalRecords = summary.TotalCount;
                PendingRecords = summary.PendingCount;
                SubmittedRecords = summary.SubmittedCount;
                UniqueSerials = summary.UniqueSerialsCount;
                ActiveOperators = summary.UniqueUsersCount;
                TopDefect = summary.TopDefect;
                TopDeviceCode = summary.TopDeviceCode;
                TopOpCo = summary.TopOpCo;

                // 2. Fetch Primary Group-By Pivot
                var (primaryCol, secondaryCol) = ParseGroupBySelection(SelectedGroupBy);
                var primaryData = await _repository.GetGroupedStatisticsAsync(
                    FromDate, ToDate, SelectedOpCo, SelectedUser, SelectedDefect, SelectedDeviceCode, SelectedStatus,
                    primaryCol, secondaryCol
                );

                GroupedResults.Clear();
                foreach (var item in primaryData)
                {
                    GroupedResults.Add(item);
                }

                // 3. Fetch Quick Cards Breakdowns
                var opcoData = await _repository.GetGroupedStatisticsAsync(FromDate, ToDate, SelectedOpCo, SelectedUser, SelectedDefect, SelectedDeviceCode, SelectedStatus, "OpCo");
                OpCoBreakdown.Clear();
                foreach (var item in opcoData.Take(6)) OpCoBreakdown.Add(item);

                var defectData = await _repository.GetGroupedStatisticsAsync(FromDate, ToDate, SelectedOpCo, SelectedUser, SelectedDefect, SelectedDeviceCode, SelectedStatus, "Defect");
                DefectBreakdown.Clear();
                foreach (var item in defectData.Take(6)) DefectBreakdown.Add(item);

                var userData = await _repository.GetGroupedStatisticsAsync(FromDate, ToDate, SelectedOpCo, SelectedUser, SelectedDefect, SelectedDeviceCode, SelectedStatus, "CreatedBy");
                UserBreakdown.Clear();
                foreach (var item in userData.Take(6)) UserBreakdown.Add(item);

                var devData = await _repository.GetGroupedStatisticsAsync(FromDate, ToDate, SelectedOpCo, SelectedUser, SelectedDefect, SelectedDeviceCode, SelectedStatus, "DeviceCode");
                DeviceCodeBreakdown.Clear();
                foreach (var item in devData.Take(6)) DeviceCodeBreakdown.Add(item);

                StatusMessage = $"📊 Statistics calculated across {TotalRecords} record(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Statistics calculation error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static (string primary, string? secondary) ParseGroupBySelection(string selection)
        {
            return selection switch
            {
                "OpCo" => ("OpCo", null),
                "Device Code" => ("DeviceCode", null),
                "Defect / Issue" => ("Defect", null),
                "Operator / User" => ("CreatedBy", null),
                "Status" => ("Status", null),
                "Daily Scan Volume (Date)" => ("date", null),
                "OpCo + Defect" => ("OpCo", "Defect"),
                "Operator + OpCo" => ("CreatedBy", "OpCo"),
                "Device Code + Defect" => ("DeviceCode", "Defect"),
                "OpCo + Device Code" => ("OpCo", "DeviceCode"),
                "Manufacturer Code" => ("ManufacturerCode", null),
                _ => ("OpCo", null)
            };
        }

        partial void OnSelectedGroupByChanged(string value)
        {
            _ = RefreshStatisticsAsync();
        }

        [RelayCommand]
        public void FilterToday()
        {
            FromDate = DateTime.Today;
            ToDate = DateTime.Today;
            _ = RefreshStatisticsAsync();
        }

        [RelayCommand]
        public void FilterYesterday()
        {
            FromDate = DateTime.Today.AddDays(-1);
            ToDate = DateTime.Today.AddDays(-1);
            _ = RefreshStatisticsAsync();
        }

        [RelayCommand]
        public void FilterThisWeek()
        {
            int diff = (7 + (DateTime.Today.DayOfWeek - DayOfWeek.Monday)) % 7;
            FromDate = DateTime.Today.AddDays(-1 * diff).Date;
            ToDate = DateTime.Today;
            _ = RefreshStatisticsAsync();
        }

        [RelayCommand]
        public void FilterThisMonth()
        {
            FromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            ToDate = DateTime.Today;
            _ = RefreshStatisticsAsync();
        }

        [RelayCommand]
        public void FilterAllTime()
        {
            FromDate = null;
            ToDate = null;
            _ = RefreshStatisticsAsync();
        }

        [RelayCommand]
        public void ResetFilters()
        {
            FromDate = null;
            ToDate = null;
            SelectedOpCo = "All";
            SelectedUser = "All";
            SelectedDefect = "All";
            SelectedDeviceCode = "All";
            SelectedStatus = "All";
            SelectedGroupBy = "OpCo";
            _ = RefreshStatisticsAsync();
        }

        [RelayCommand]
        public void ExportStatisticsExcel()
        {
            if (GroupedResults.Count == 0)
            {
                StatusMessage = "No statistics to export.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"Statistics_Report_{SelectedGroupBy.Replace(" ", "_").Replace("/", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Export Statistics Report"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"RDES Statistics Report - Grouped by {SelectedGroupBy}");
                    sb.AppendLine($"Generated: {DateTime.Now:g} by {Environment.UserName}");
                    sb.AppendLine($"Filters: OpCo={SelectedOpCo}, User={SelectedUser}, Defect={SelectedDefect}, DeviceCode={SelectedDeviceCode}, Status={SelectedStatus}");
                    sb.AppendLine($"Total Records: {TotalRecords}, Pending: {PendingRecords}, Submitted: {SubmittedRecords}");
                    sb.AppendLine();
                    sb.AppendLine("Category,SubCategory,Quantity,Percentage");

                    foreach (var item in GroupedResults)
                    {
                        sb.AppendLine($"\"{item.Key.Replace("\"", "\"\"")}\",\"{item.SubKey.Replace("\"", "\"\"")}\",{item.Count},{item.Percentage:F2}%");
                    }

                    File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                    StatusMessage = $"✅ Successfully exported statistics report to {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export error: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public void CopySummaryToClipboard()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"📊 RDES Statistics Summary - {SelectedGroupBy}");
                sb.AppendLine($"Total Count: {TotalRecords} | Active/Pending: {PendingRecords} | Submitted: {SubmittedRecords}");
                sb.AppendLine($"Top OpCo: {TopOpCo} | Top Defect: {TopDefect} | Top Device Code: {TopDeviceCode}");
                sb.AppendLine("--------------------------------------------------");
                foreach (var item in GroupedResults)
                {
                    string sub = !string.IsNullOrEmpty(item.SubKey) ? $" ({item.SubKey})" : "";
                    sb.AppendLine($"{item.Key}{sub}: {item.Count} ({item.FormattedPercentage})");
                }

                Clipboard.SetText(sb.ToString());
                StatusMessage = "📋 Statistics summary copied to clipboard!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Copy error: {ex.Message}";
            }
        }
    }
}
