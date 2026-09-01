using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDES.App.Models;
using RDES.App.Services;

namespace RDES.App.ViewModels
{
    public partial class EntryViewModel : ObservableObject
    {
        private readonly DeviceRepository _repository;
        private readonly ConfigService _configService;
        private readonly BarcodeParserService _barcodeParser = new();

        [ObservableProperty]
        private long _currentRecordId = 0;

        [ObservableProperty]
        private string _serialNumber = string.Empty;

        [ObservableProperty]
        private string _moduleNumber = string.Empty;

        [ObservableProperty]
        private string _selectedDefect = string.Empty;

        [ObservableProperty]
        private string _customDefect = string.Empty;

        [ObservableProperty]
        private string _deviceCode = string.Empty;

        [ObservableProperty]
        private string _manufacturerCode = string.Empty;

        [ObservableProperty]
        private string _lookupPrefix = string.Empty;

        [ObservableProperty]
        private string _meterType = string.Empty;

        [ObservableProperty]
        private string _barcodeDeconstructedInfo = string.Empty;

        [ObservableProperty]
        private string _status = "Pending";

        [ObservableProperty]
        private string _selectedOpCo = "OH - RMA";

        [ObservableProperty]
        private string _catalog = string.Empty;

        [ObservableProperty]
        private string _customerInput = string.Empty;

        [ObservableProperty]
        private string _notes = string.Empty;

        [ObservableProperty]
        private int _quantity = 1;

        [ObservableProperty]
        private bool _isEditMode = false;

        [ObservableProperty]
        private bool _isBusy = false;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isSuccessMessage = true;

        [ObservableProperty]
        private string _duplicateWarning = string.Empty;

        [ObservableProperty]
        private bool _hasDuplicateWarning = false;

        [ObservableProperty]
        private DeviceRecord? _existingDuplicateRecord = null;

        public string ActiveUserName => Environment.UserName;
        public string ActiveMachineName => Environment.MachineName;

        public ObservableCollection<string> DefectList { get; } = new();
        public ObservableCollection<string> OpCoList { get; } = new();
        public ObservableCollection<string> StatusOptions { get; } = new() { "Pending", "In Progress", "Repaired", "Scrapped", "Closed", "Approved" };
        public ObservableCollection<DeviceRecord> RecentEntries { get; } = new();

        public event Action? RequestFocusSerial;

        public EntryViewModel(DeviceRepository repository, ConfigService configService)
        {
            _repository = repository;
            _configService = configService;
        }

        public async Task InitializeAsync()
        {
            await LoadDefectOptionsAsync();
            await LoadOpCoOptionsAsync();
            await RefreshRecentEntriesAsync();
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
                StatusMessage = $"Error loading OpCo list: {ex.Message}";
                IsSuccessMessage = false;
            }
        }

        public async Task LoadDefectOptionsAsync()
        {
            try
            {
                var options = await _repository.GetDefectOptionsAsync();
                DefectList.Clear();
                foreach (var opt in options)
                {
                    DefectList.Add(opt.Name);
                }
                if (DefectList.Count > 0 && string.IsNullOrEmpty(SelectedDefect))
                {
                    SelectedDefect = DefectList.First();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading defect list: {ex.Message}";
                IsSuccessMessage = false;
            }
        }

        public async Task RefreshRecentEntriesAsync()
        {
            try
            {
                var recent = await _repository.GetRecentRecordsAsync(10);
                RecentEntries.Clear();
                foreach (var r in recent)
                {
                    RecentEntries.Add(r);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to refresh recent entries: {ex.Message}");
            }
        }

        partial void OnSerialNumberChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                BarcodeDeconstructedInfo = string.Empty;
                return;
            }

            if (_configService.CurrentConfig.AutoUppercaseSerials)
            {
                string upper = value.ToUpperInvariant();
                if (upper != value)
                {
                    SerialNumber = upper;
                    return;
                }
            }

            // Check if input is a composite barcode (e.g. 1ND988181154NVD06)
            var parsed = _barcodeParser.Parse(value);
            if (parsed.IsCompositeBarcode && !IsEditMode)
            {
                LookupPrefix = parsed.LookupPrefix;
                if (!string.IsNullOrEmpty(parsed.ManufacturerCode)) ManufacturerCode = parsed.ManufacturerCode;
                if (!string.IsNullOrEmpty(parsed.DeviceCode)) DeviceCode = parsed.DeviceCode;
                if (!string.IsNullOrEmpty(parsed.MeterType)) MeterType = parsed.MeterType;

                string typeInfo = !string.IsNullOrEmpty(parsed.MeterType) ? $" ({parsed.MeterType})" : "";
                BarcodeDeconstructedInfo = $"⚡ Barcode Auto-Split: Prefix: {parsed.LookupPrefix} | Manu: {parsed.ManufacturerCode} | Serial: {parsed.SerialNumber} | Device: {parsed.DeviceCode}{typeInfo}";

                // Update SerialNumber to clean numeric/standard serial without recursion
                if (SerialNumber != parsed.SerialNumber)
                {
                    SerialNumber = parsed.SerialNumber;
                    return;
                }
            }
            else if (!parsed.IsCompositeBarcode && string.IsNullOrEmpty(BarcodeDeconstructedInfo))
            {
                // Reset deconstructed info for plain serial
                BarcodeDeconstructedInfo = string.Empty;
            }

            // Check for duplicate in background
            _ = CheckDuplicateSerialAsync(value);
        }

        private async Task CheckDuplicateSerialAsync(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial) || IsEditMode)
            {
                HasDuplicateWarning = false;
                DuplicateWarning = string.Empty;
                ExistingDuplicateRecord = null;
                return;
            }

            var existing = await _repository.GetBySerialNumberAsync(serial);
            if (existing != null && existing.Id != CurrentRecordId)
            {
                ExistingDuplicateRecord = existing;
                HasDuplicateWarning = true;
                DuplicateWarning = $"⚠️ Serial '{serial}' exists (Entered by {existing.CreatedBy} on {existing.CreatedAt:g}, Defect: {existing.Defect}).";
            }
            else
            {
                HasDuplicateWarning = false;
                DuplicateWarning = string.Empty;
                ExistingDuplicateRecord = null;
            }
        }

        [RelayCommand]
        public void LoadExistingDuplicate()
        {
            if (ExistingDuplicateRecord != null)
            {
                LoadRecordForEdit(ExistingDuplicateRecord);
            }
        }

        [RelayCommand]
        public async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(SerialNumber))
            {
                StatusMessage = "Serial Number is required.";
                IsSuccessMessage = false;
                RequestFocusSerial?.Invoke();
                return;
            }

            IsBusy = true;
            try
            {
                string defectToSave = SelectedDefect;
                if (SelectedDefect == "Other*" && !string.IsNullOrWhiteSpace(CustomDefect))
                {
                    defectToSave = CustomDefect.Trim();
                }

                if (IsEditMode)
                {
                    var record = new DeviceRecord
                    {
                        Id = CurrentRecordId,
                        SerialNumber = SerialNumber.Trim(),
                        ModuleNumber = ModuleNumber.Trim(),
                        Defect = defectToSave,
                        DeviceCode = DeviceCode.Trim(),
                        ManufacturerCode = ManufacturerCode.Trim(),
                        ModType = !string.IsNullOrEmpty(MeterType) ? MeterType : string.Empty,
                        Status = Status,
                        OpCo = SelectedOpCo,
                        Catalog = Catalog.Trim(),
                        CustomerInput = CustomerInput.Trim(),
                        Notes = Notes.Trim(),
                        Quantity = Quantity
                    };

                    bool ok = await _repository.UpdateRecordAsync(record);
                    if (ok)
                    {
                        StatusMessage = $"✅ Record for serial '{record.SerialNumber}' updated successfully!";
                        IsSuccessMessage = true;
                        ClearForm();
                    }
                    else
                    {
                        StatusMessage = "Failed to update record.";
                        IsSuccessMessage = false;
                    }
                }
                else
                {
                    var record = new DeviceRecord
                    {
                        SerialNumber = SerialNumber.Trim(),
                        ModuleNumber = ModuleNumber.Trim(),
                        Defect = defectToSave,
                        DeviceCode = DeviceCode.Trim(),
                        ManufacturerCode = ManufacturerCode.Trim(),
                        ModType = !string.IsNullOrEmpty(MeterType) ? MeterType : string.Empty,
                        Status = Status,
                        OpCo = SelectedOpCo,
                        Catalog = Catalog.Trim(),
                        CustomerInput = CustomerInput.Trim(),
                        Notes = Notes.Trim(),
                        Quantity = Quantity
                    };

                    long newId = await _repository.InsertRecordAsync(record);
                    StatusMessage = $"✅ Serial '{record.SerialNumber}' saved (ID: {newId}) by {Environment.UserName}";
                    IsSuccessMessage = true;
                    ClearForm();
                }

                await RefreshRecentEntriesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving record: {ex.Message}";
                IsSuccessMessage = false;
            }
            finally
            {
                IsBusy = false;
                RequestFocusSerial?.Invoke();
            }
        }

        [RelayCommand]
        public void ClearForm()
        {
            CurrentRecordId = 0;
            SerialNumber = string.Empty;
            ModuleNumber = string.Empty;
            if (DefectList.Count > 0) SelectedDefect = DefectList.First();
            CustomDefect = string.Empty;
            DeviceCode = string.Empty;
            ManufacturerCode = string.Empty;
            LookupPrefix = string.Empty;
            MeterType = string.Empty;
            BarcodeDeconstructedInfo = string.Empty;
            Status = "Pending";
            Catalog = string.Empty;
            CustomerInput = string.Empty;
            Notes = string.Empty;
            Quantity = 1;
            IsEditMode = false;
            HasDuplicateWarning = false;
            DuplicateWarning = string.Empty;
            ExistingDuplicateRecord = null;
            RequestFocusSerial?.Invoke();
        }

        public void LoadRecordForEdit(DeviceRecord record)
        {
            CurrentRecordId = record.Id;
            SerialNumber = record.SerialNumber;
            ModuleNumber = record.ModuleNumber;
            
            if (DefectList.Contains(record.Defect))
            {
                SelectedDefect = record.Defect;
                CustomDefect = string.Empty;
            }
            else
            {
                if (DefectList.Contains("Other*"))
                {
                    SelectedDefect = "Other*";
                }
                CustomDefect = record.Defect;
            }

            DeviceCode = record.DeviceCode;
            ManufacturerCode = record.ManufacturerCode;
            MeterType = record.ModType;
            BarcodeDeconstructedInfo = string.Empty;
            Status = record.Status;
            if (!string.IsNullOrEmpty(record.OpCo) && OpCoList.Contains(record.OpCo))
            {
                SelectedOpCo = record.OpCo;
            }
            Catalog = record.Catalog;
            CustomerInput = record.CustomerInput;
            Notes = record.Notes;
            Quantity = record.Quantity > 0 ? record.Quantity : 1;
            IsEditMode = true;
            HasDuplicateWarning = false;
            DuplicateWarning = string.Empty;
            StatusMessage = $"Editing record ID #{record.Id} (Serial: {record.SerialNumber})";
            IsSuccessMessage = true;
            RequestFocusSerial?.Invoke();
        }
    }
}
