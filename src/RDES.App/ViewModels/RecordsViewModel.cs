using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RDES.App.Models;
using RDES.App.Services;

namespace RDES.App.ViewModels
{
    public partial class RecordsViewModel : ObservableObject
    {
        private readonly DeviceRepository _repository;
        private readonly ExcelService _excelService;
        private readonly ConfigService _configService;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private string _selectedStatus = "Pending";

        [ObservableProperty]
        private string _selectedDefect = "All";

        [ObservableProperty]
        private string _selectedOpCo = "All";

        [ObservableProperty]
        private DateTime? _fromDate = null;

        [ObservableProperty]
        private DateTime? _toDate = null;

        [ObservableProperty]
        private DeviceRecord? _selectedRecord;

        [ObservableProperty]
        private bool _isBusy = false;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private int _totalRecordsCount = 0;

        [ObservableProperty]
        private int _filteredRecordsCount = 0;

        [ObservableProperty]
        private int _selectedItemsCount = 0;

        [ObservableProperty]
        private bool _isAllSelected = false;

        public ObservableCollection<DeviceRecord> Records { get; } = new();
        public ObservableCollection<string> StatusFilterList { get; } = new() { "Pending", "Submitted", "All" };
        public ObservableCollection<string> DefectFilterList { get; } = new() { "All" };
        public ObservableCollection<string> OpCoFilterList { get; } = new() { "All" };

        public event Action<DeviceRecord>? RequestEditRecord;

        public RecordsViewModel(DeviceRepository repository, ExcelService excelService, ConfigService configService)
        {
            _repository = repository;
            _excelService = excelService;
            _configService = configService;
        }

        public async Task InitializeAsync()
        {
            await LoadDefectFiltersAsync();
            await LoadOpCoFiltersAsync();
            await SearchAsync();
        }

        public async Task LoadOpCoFiltersAsync()
        {
            try
            {
                var opcos = await _repository.GetOpCoOptionsAsync();
                OpCoFilterList.Clear();
                OpCoFilterList.Add("All");
                foreach (var o in opcos)
                {
                    OpCoFilterList.Add(o.Name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load OpCo filters: {ex.Message}");
            }
        }

        public async Task LoadDefectFiltersAsync()
        {
            try
            {
                var defects = await _repository.GetDefectOptionsAsync();
                DefectFilterList.Clear();
                DefectFilterList.Add("All");
                foreach (var d in defects)
                {
                    DefectFilterList.Add(d.Name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load defect filters: {ex.Message}");
            }
        }

        [RelayCommand]
        public void SelectOpCo(string? opco)
        {
            SelectedOpCo = !string.IsNullOrEmpty(opco) ? opco : "All";
            _ = SearchAsync();
        }

        [RelayCommand]
        public void SelectStatus(string? status)
        {
            SelectedStatus = !string.IsNullOrEmpty(status) ? status : "Pending";
            _ = SearchAsync();
        }

        [RelayCommand]
        public async Task SearchAsync()
        {
            IsBusy = true;
            try
            {
                var results = await _repository.SearchRecordsAsync(
                    SearchQuery,
                    SelectedStatus,
                    SelectedDefect,
                    SelectedOpCo,
                    FromDate,
                    ToDate,
                    _configService.CurrentConfig.PageSize
                );

                // Unhook previous property listeners
                foreach (var r in Records)
                {
                    r.PropertyChanged -= OnRecordPropertyChanged;
                }

                Records.Clear();
                foreach (var item in results)
                {
                    item.PropertyChanged += OnRecordPropertyChanged;
                    Records.Add(item);
                }

                FilteredRecordsCount = Records.Count;
                var stats = await _repository.GetStatisticsAsync();
                TotalRecordsCount = stats.TotalCount;
                UpdateSelectedCount();

                string statusDesc = SelectedStatus == "Pending" ? "Active (Pending)" : (SelectedStatus == "Submitted" ? "Submitted" : "All");
                StatusMessage = $"Displaying {FilteredRecordsCount} {statusDesc} record(s). Total in DB: {TotalRecordsCount}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Search error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnRecordPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceRecord.IsSelected))
            {
                UpdateSelectedCount();
            }
        }

        public void UpdateSelectedCount()
        {
            SelectedItemsCount = Records.Count(r => r.IsSelected);
            IsAllSelected = Records.Count > 0 && SelectedItemsCount == Records.Count;
        }

        [RelayCommand]
        public void SelectAll()
        {
            foreach (var r in Records)
            {
                r.IsSelected = true;
            }
            UpdateSelectedCount();
        }

        [RelayCommand]
        public void DeselectAll()
        {
            foreach (var r in Records)
            {
                r.IsSelected = false;
            }
            UpdateSelectedCount();
        }

        [RelayCommand]
        public void ToggleSelectAll()
        {
            bool newState = !IsAllSelected;
            foreach (var r in Records)
            {
                r.IsSelected = newState;
            }
            UpdateSelectedCount();
        }

        private List<long> GetTargetIds()
        {
            var checkedIds = Records.Where(r => r.IsSelected).Select(r => r.Id).ToList();
            if (checkedIds.Count > 0) return checkedIds;
            if (SelectedRecord != null) return new List<long> { SelectedRecord.Id };
            return new List<long>();
        }

        private List<DeviceRecord> GetTargetRecords()
        {
            var checkedRecords = Records.Where(r => r.IsSelected).ToList();
            if (checkedRecords.Count > 0) return checkedRecords;
            if (SelectedRecord != null) return new List<DeviceRecord> { SelectedRecord };
            return Records.ToList();
        }

        [RelayCommand]
        public void ResetFilters()
        {
            SearchQuery = string.Empty;
            SelectedStatus = "Pending";
            SelectedDefect = "All";
            SelectedOpCo = "All";
            FromDate = null;
            ToDate = null;
            _ = SearchAsync();
        }

        [RelayCommand]
        public void FilterToday()
        {
            FromDate = DateTime.Today;
            ToDate = DateTime.Today;
            _ = SearchAsync();
        }

        [RelayCommand]
        public void FilterThisWeek()
        {
            int diff = (7 + (DateTime.Today.DayOfWeek - DayOfWeek.Monday)) % 7;
            FromDate = DateTime.Today.AddDays(-1 * diff).Date;
            ToDate = DateTime.Today;
            _ = SearchAsync();
        }

        [RelayCommand]
        public void EditSelected()
        {
            var target = Records.FirstOrDefault(r => r.IsSelected) ?? SelectedRecord;
            if (target != null)
            {
                RequestEditRecord?.Invoke(target);
            }
        }

        [RelayCommand]
        public async Task MarkSelectedAsSubmittedAsync()
        {
            var ids = GetTargetIds();
            if (ids.Count == 0)
            {
                StatusMessage = "Please select one or more records to mark as Submitted.";
                return;
            }

            int count = await _repository.UpdateStatusBatchAsync(ids, "Submitted");
            if (count > 0)
            {
                StatusMessage = $"✅ Successfully marked {count} record(s) as Submitted and cleared from active view.";
                await SearchAsync();
            }
        }

        [RelayCommand]
        public async Task MarkBatchAsSubmittedAsync()
        {
            if (Records.Count == 0)
            {
                StatusMessage = "No records in current view to submit.";
                return;
            }

            var pendingIds = Records.Where(r => r.Status != "Submitted").Select(r => r.Id).ToList();
            if (pendingIds.Count == 0)
            {
                StatusMessage = "All records in current view are already marked as Submitted.";
                return;
            }

            int count = await _repository.UpdateStatusBatchAsync(pendingIds, "Submitted");
            StatusMessage = $"✅ Successfully marked {count} record(s) as Submitted and cleared from active view.";
            await SearchAsync();
        }

        [RelayCommand]
        public async Task MarkSelectedAsPendingAsync()
        {
            var ids = GetTargetIds();
            if (ids.Count == 0)
            {
                StatusMessage = "Please select one or more records to restore to Pending.";
                return;
            }

            int count = await _repository.UpdateStatusBatchAsync(ids, "Pending");
            if (count > 0)
            {
                StatusMessage = $"✅ Successfully restored {count} record(s) to Pending.";
                await SearchAsync();
            }
        }

        [RelayCommand]
        public async Task DeleteSelectedAsync()
        {
            var ids = GetTargetIds();
            if (ids.Count == 0)
            {
                StatusMessage = "Please select one or more records to delete.";
                return;
            }

            int deletedCount = 0;
            foreach (var id in ids)
            {
                if (await _repository.DeleteRecordAsync(id))
                {
                    deletedCount++;
                }
            }

            StatusMessage = $"✅ Successfully deleted {deletedCount} record(s).";
            await SearchAsync();
        }

        [RelayCommand]
        public void ExportExcel()
        {
            var exportList = GetTargetRecords();
            if (exportList.Count == 0)
            {
                StatusMessage = "No records to export.";
                return;
            }

            string opcoTag = SelectedOpCo != "All" ? $"{SelectedOpCo.Replace(" ", "_")}_" : "";
            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"{opcoTag}Device_Records_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                Title = "Export Records to Excel"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _excelService.ExportToExcel(exportList, dialog.FileName);
                    StatusMessage = $"✅ Successfully exported {exportList.Count} records to {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export error: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public void ExportCsv()
        {
            var exportList = GetTargetRecords();
            if (exportList.Count == 0)
            {
                StatusMessage = "No records to export.";
                return;
            }

            string opcoTag = SelectedOpCo != "All" ? $"{SelectedOpCo.Replace(" ", "_")}_" : "";
            var dialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"{opcoTag}Device_Records_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Export Records to CSV"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _excelService.ExportToCsv(exportList, dialog.FileName);
                    StatusMessage = $"✅ Successfully exported {exportList.Count} records to {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export error: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public void ExportAep()
        {
            var exportList = GetTargetRecords();
            if (exportList.Count == 0)
            {
                StatusMessage = "No records to export.";
                return;
            }

            string opcoTag = SelectedOpCo != "All" ? $"{SelectedOpCo.Replace(" ", "_")}_" : "";
            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"{opcoTag}AEP_RMA_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                Title = "Export Records to AEP RMA Format"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _excelService.ExportToAepFormat(exportList, dialog.FileName);
                    StatusMessage = $"✅ Successfully exported {exportList.Count} records in AEP format to {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"AEP Export error: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public void ExportAclara()
        {
            var exportList = GetTargetRecords();
            if (exportList.Count == 0)
            {
                StatusMessage = "No records to export.";
                return;
            }

            string opcoTag = SelectedOpCo != "All" ? $"{SelectedOpCo.Replace(" ", "_")}_" : "";
            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"{opcoTag}Aclara_RMA_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                Title = "Export Records to Aclara RMA Format"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _excelService.ExportToAclaraFormat(exportList, dialog.FileName);
                    StatusMessage = $"✅ Successfully exported {exportList.Count} records in Aclara format to {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Aclara Export error: {ex.Message}";
                }
            }
        }
    }
}
