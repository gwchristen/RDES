using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RDES.App.Models;
using RDES.App.Services;

namespace RDES.App.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly DeviceRepository _repository;

        [ObservableProperty]
        private string _databasePath = string.Empty;

        [ObservableProperty]
        private bool _autoUppercaseSerials = true;

        [ObservableProperty]
        private int _busyTimeoutMs = 10000;

        [ObservableProperty]
        private string _testResult = string.Empty;

        [ObservableProperty]
        private bool _isConnectionSuccess = false;

        [ObservableProperty]
        private bool _isBusy = false;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string _newDefectName = string.Empty;

        [ObservableProperty]
        private string _newOpCoName = string.Empty;

        [ObservableProperty]
        private bool _isSettingsLocked = true;

        [ObservableProperty]
        private string _pinInput = string.Empty;

        [ObservableProperty]
        private string _newPinInput = string.Empty;

        [ObservableProperty]
        private string _pinMessage = string.Empty;

        [ObservableProperty]
        private bool _isPinPromptVisible = false;

        public bool IsSettingsUnlocked => !IsSettingsLocked;

        public ObservableCollection<OpCoOption> OpCoList { get; } = new();
        public ObservableCollection<DefectOption> DefectList { get; } = new();

        public string ActiveUserName => Environment.UserName;
        public string ActiveMachineName => Environment.MachineName;

        public event Action? DatabasePathChanged;
        public event Action? OpCoListChanged;
        public event Action? DefectListChanged;

        public SettingsViewModel(ConfigService configService, DatabaseService databaseService, DeviceRepository repository)
        {
            _configService = configService;
            _databaseService = databaseService;
            _repository = repository;

            LoadSettings();
            _ = LoadOpCosAsync();
            _ = LoadDefectsAsync();
        }

        [RelayCommand]
        public void ShowPinPrompt()
        {
            PinInput = string.Empty;
            PinMessage = string.Empty;
            IsPinPromptVisible = true;
        }

        [RelayCommand]
        public void CancelPinPrompt()
        {
            PinInput = string.Empty;
            PinMessage = string.Empty;
            IsPinPromptVisible = false;
        }

        [RelayCommand]
        public void UnlockSettings()
        {
            var config = _configService.CurrentConfig;
            string correctPin = string.IsNullOrWhiteSpace(config.AdminPin) ? "1234" : config.AdminPin.Trim();

            if (string.Equals(PinInput?.Trim(), correctPin, StringComparison.Ordinal))
            {
                IsSettingsLocked = false;
                IsPinPromptVisible = false;
                PinInput = string.Empty;
                PinMessage = string.Empty;
                StatusMessage = "🔓 Administrator mode unlocked. You can now modify shared configuration.";
                OnPropertyChanged(nameof(IsSettingsUnlocked));
            }
            else
            {
                PinMessage = "❌ Incorrect Admin PIN. Default is '1234'.";
            }
        }

        [RelayCommand]
        public void LockSettings()
        {
            IsSettingsLocked = true;
            IsPinPromptVisible = false;
            PinInput = string.Empty;
            PinMessage = string.Empty;
            StatusMessage = "🔒 Settings locked.";
            OnPropertyChanged(nameof(IsSettingsUnlocked));
        }

        [RelayCommand]
        public void ChangeAdminPin()
        {
            if (string.IsNullOrWhiteSpace(NewPinInput) || NewPinInput.Trim().Length < 4)
            {
                StatusMessage = "PIN must be at least 4 characters.";
                return;
            }

            var config = _configService.CurrentConfig;
            config.AdminPin = NewPinInput.Trim();
            _configService.SaveConfig(config);
            NewPinInput = string.Empty;
            StatusMessage = "✅ Admin PIN successfully updated.";
        }

        public async Task LoadOpCosAsync()
        {
            try
            {
                var list = await _repository.GetOpCoOptionsAsync();
                OpCoList.Clear();
                foreach (var item in list)
                {
                    OpCoList.Add(item);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load OpCos: {ex.Message}");
            }
        }

        public async Task LoadDefectsAsync()
        {
            try
            {
                var list = await _repository.GetDefectOptionsAsync();
                DefectList.Clear();
                foreach (var item in list)
                {
                    DefectList.Add(item);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load Defects: {ex.Message}");
            }
        }

        public void LoadSettings()
        {
            var config = _configService.CurrentConfig;
            DatabasePath = config.DatabasePath;
            AutoUppercaseSerials = config.AutoUppercaseSerials;
            BusyTimeoutMs = config.BusyTimeoutMs;
        }

        [RelayCommand]
        public void BrowseDatabasePath()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                FileName = Path.GetFileName(DatabasePath) ?? "rdes_shared.db",
                Title = "Select or Create Shared Database File"
            };

            if (dialog.ShowDialog() == true)
            {
                DatabasePath = dialog.FileName;
            }
        }

        [RelayCommand]
        public async Task TestConnectionAsync()
        {
            IsBusy = true;
            try
            {
                var (success, msg) = await _databaseService.TestConnectionAsync(DatabasePath);
                IsConnectionSuccess = success;
                TestResult = msg;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task SaveSettingsAsync()
        {
            IsBusy = true;
            try
            {
                var config = _configService.CurrentConfig;
                bool pathChanged = !string.Equals(config.DatabasePath, DatabasePath, StringComparison.OrdinalIgnoreCase);

                config.DatabasePath = DatabasePath;
                config.AutoUppercaseSerials = AutoUppercaseSerials;
                config.BusyTimeoutMs = BusyTimeoutMs;

                _configService.SaveConfig(config);

                if (pathChanged)
                {
                    _databaseService.UpdateDatabasePath(DatabasePath);
                    await _databaseService.EnsureDatabaseInitializedAsync();
                    DatabasePathChanged?.Invoke();
                }

                StatusMessage = "✅ Settings saved successfully!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving settings: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task BackupDatabaseAsync()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Destination Folder for Database Backup"
            };

            if (dialog.ShowDialog() == true)
            {
                IsBusy = true;
                try
                {
                    var (success, msg) = await _databaseService.BackupDatabaseAsync(dialog.FolderName);
                    StatusMessage = success ? $"✅ {msg}" : $"❌ {msg}";
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        [RelayCommand]
        public async Task AddNewDefectAsync()
        {
            if (string.IsNullOrWhiteSpace(NewDefectName)) return;

            try
            {
                var opt = new DefectOption
                {
                    Name = NewDefectName.Trim(),
                    Category = "Custom",
                    IsActive = true
                };

                bool ok = await _repository.AddDefectOptionAsync(opt);
                if (ok)
                {
                    StatusMessage = $"✅ Added defect '{NewDefectName}' to lookup list.";
                    NewDefectName = string.Empty;
                    await LoadDefectsAsync();
                    DefectListChanged?.Invoke();
                }
                else
                {
                    StatusMessage = "Defect already exists in lookup list.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to add defect: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task DeleteDefectAsync(DefectOption? defect)
        {
            if (defect == null) return;

            try
            {
                bool ok = await _repository.DeleteDefectOptionAsync(defect.Id);
                if (ok)
                {
                    StatusMessage = $"Removed defect '{defect.Name}'.";
                    await LoadDefectsAsync();
                    DefectListChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to remove defect: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task AddNewOpCoAsync()
        {
            if (string.IsNullOrWhiteSpace(NewOpCoName)) return;

            try
            {
                var opt = new OpCoOption
                {
                    Name = NewOpCoName.Trim(),
                    SortOrder = OpCoList.Count + 1,
                    IsActive = true
                };

                bool ok = await _repository.AddOpCoOptionAsync(opt);
                if (ok)
                {
                    StatusMessage = $"✅ Added OpCo '{NewOpCoName}'.";
                    NewOpCoName = string.Empty;
                    await LoadOpCosAsync();
                    OpCoListChanged?.Invoke();
                }
                else
                {
                    StatusMessage = "OpCo already exists.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to add OpCo: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task DeleteOpCoAsync(OpCoOption? opco)
        {
            if (opco == null) return;

            try
            {
                bool ok = await _repository.DeleteOpCoOptionAsync(opco.Id);
                if (ok)
                {
                    StatusMessage = $"Removed OpCo '{opco.Name}'.";
                    await LoadOpCosAsync();
                    OpCoListChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to remove OpCo: {ex.Message}";
            }
        }
    }
}
