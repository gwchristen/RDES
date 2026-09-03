using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public partial class BulkImportViewModel : ObservableObject
    {
        private readonly DeviceRepository _repository;
        private readonly ExcelService _excelService;
        private readonly ConfigService _configService;

        [ObservableProperty]
        private string _selectedFilePath = string.Empty;

        [ObservableProperty]
        private string _selectedSheet = string.Empty;

        [ObservableProperty]
        private string _selectedOpCo = "OH - RMA";

        [ObservableProperty]
        private bool _overwriteDuplicates = false;

        [ObservableProperty]
        private bool _isBusy = false;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private int _previewCount = 0;

        [ObservableProperty]
        private bool _hasParsedRecords = false;

        public ObservableCollection<string> AvailableSheets { get; } = new();
        public ObservableCollection<string> OpCoList { get; } = new();
        public ObservableCollection<DeviceRecord> PreviewRecords { get; } = new();

        private List<DeviceRecord> _allParsedRecords = new();

        public BulkImportViewModel(DeviceRepository repository, ExcelService excelService, ConfigService configService)
        {
            _repository = repository;
            _excelService = excelService;
            _configService = configService;
        }

        public async Task LoadOpCoOptionsAsync()
        {
            try
            {
                var options = await _repository.GetOpCoOptionsAsync();
                OpCoList.Clear();
                foreach (var opt in options)
                {
                    OpCoList.Add(opt.Name);
                }

                if (OpCoList.Count > 0 && (string.IsNullOrEmpty(SelectedOpCo) || !OpCoList.Contains(SelectedOpCo)))
                {
                    SelectedOpCo = OpCoList.First();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading OpCo options: {ex.Message}";
            }
        }

        partial void OnSelectedOpCoChanged(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && _allParsedRecords.Count > 0)
            {
                foreach (var r in _allParsedRecords)
                {
                    r.OpCo = value;
                }
                foreach (var r in PreviewRecords)
                {
                    r.OpCo = value;
                }
            }
        }

        [RelayCommand]
        public void BrowseFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Excel Files (*.xlsm;*.xlsx;*.xls)|*.xlsm;*.xlsx;*.xls|Macro-Enabled Excel (*.xlsm)|*.xlsm|Excel Workbook (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                Title = "Select Spreadsheet to Import"
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedFilePath = dialog.FileName;
                LoadSheets();
            }
        }

        private void LoadSheets()
        {
            if (string.IsNullOrWhiteSpace(SelectedFilePath) || !File.Exists(SelectedFilePath)) return;

            try
            {
                var sheets = _excelService.GetSheetNames(SelectedFilePath);
                AvailableSheets.Clear();
                foreach (var s in sheets)
                {
                    AvailableSheets.Add(s);
                }

                // Default selection
                string preferred = _configService.CurrentConfig.LastUsedSheetName;
                if (AvailableSheets.Contains(preferred))
                {
                    SelectedSheet = preferred;
                }
                else if (AvailableSheets.Contains("RMA Entry"))
                {
                    SelectedSheet = "RMA Entry";
                }
                else if (AvailableSheets.Contains("AEP"))
                {
                    SelectedSheet = "AEP";
                }
                else if (AvailableSheets.Count > 0)
                {
                    SelectedSheet = AvailableSheets.First();
                }

                ParseSpreadsheet();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to read sheets: {ex.Message}";
            }
        }

        partial void OnSelectedSheetChanged(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _configService.CurrentConfig.LastUsedSheetName = value;
                _configService.SaveConfig(_configService.CurrentConfig);
                ParseSpreadsheet();
            }
        }

        [RelayCommand]
        public void ParseSpreadsheet()
        {
            if (string.IsNullOrWhiteSpace(SelectedFilePath) || !File.Exists(SelectedFilePath)) return;

            IsBusy = true;
            try
            {
                _allParsedRecords = _excelService.ImportFromSpreadsheet(SelectedFilePath, SelectedSheet);

                if (!string.IsNullOrWhiteSpace(SelectedOpCo))
                {
                    foreach (var r in _allParsedRecords)
                    {
                        r.OpCo = SelectedOpCo;
                    }
                }

                PreviewRecords.Clear();
                foreach (var r in _allParsedRecords.Take(100))
                {
                    PreviewRecords.Add(r);
                }

                PreviewCount = _allParsedRecords.Count;
                HasParsedRecords = _allParsedRecords.Count > 0;
                StatusMessage = $"Parsed {_allParsedRecords.Count} device record(s) from sheet '{SelectedSheet}' for OpCo '{SelectedOpCo}'. Showing preview of first {PreviewRecords.Count}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error parsing spreadsheet: {ex.Message}";
                HasParsedRecords = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ExecuteImportAsync()
        {
            if (_allParsedRecords.Count == 0)
            {
                StatusMessage = "No records parsed to import.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(SelectedOpCo))
            {
                foreach (var r in _allParsedRecords)
                {
                    r.OpCo = SelectedOpCo;
                }
            }

            IsBusy = true;
            StatusMessage = $"Importing {_allParsedRecords.Count} records into database for OpCo '{SelectedOpCo}'...";
            try
            {
                var result = await _repository.BulkInsertAsync(_allParsedRecords, OverwriteDuplicates);

                StatusMessage = $"✅ Import complete for '{SelectedOpCo}'! Inserted: {result.InsertedCount}, Updated: {result.UpdatedCount}, Skipped: {result.SkippedDuplicates}, Errors: {result.Errors.Count}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Import failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
