using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDES.App.Models;
using RDES.App.Services;

namespace RDES.App.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly DeviceRepository _repository;
        private readonly ExcelService _excelService;

        public EntryViewModel EntryVM { get; }
        public RecordsViewModel RecordsVM { get; }
        public StatisticsViewModel StatisticsVM { get; }
        public BulkImportViewModel BulkImportVM { get; }
        public SettingsViewModel SettingsVM { get; }

        [ObservableProperty]
        private int _selectedTabIndex = 0;

        [ObservableProperty]
        private object _currentViewModel;

        [ObservableProperty]
        private string _currentUserName = Environment.UserName;

        [ObservableProperty]
        private string _currentMachineName = Environment.MachineName;

        [ObservableProperty]
        private string _databasePath = string.Empty;

        [ObservableProperty]
        private bool _isDatabaseConnected = false;

        [ObservableProperty]
        private string _connectionStatusText = "Connecting...";

        [ObservableProperty]
        private int _totalDatabaseRecords = 0;

        [ObservableProperty]
        private int _todayRecordsCount = 0;

        [ObservableProperty]
        private int _pendingRecordsCount = 0;

        [ObservableProperty]
        private bool _isDarkMode = false;

        public MainViewModel()
        {
            _configService = new ConfigService();
            _databaseService = new DatabaseService(_configService);
            _repository = new DeviceRepository(_databaseService);
            _excelService = new ExcelService();

            EntryVM = new EntryViewModel(_repository, _configService);
            RecordsVM = new RecordsViewModel(_repository, _excelService, _configService);
            StatisticsVM = new StatisticsViewModel(_repository, _excelService, _configService);
            BulkImportVM = new BulkImportViewModel(_repository, _excelService, _configService);
            SettingsVM = new SettingsViewModel(_configService, _databaseService, _repository);

            _currentViewModel = EntryVM;
            DatabasePath = _databaseService.DatabasePath;

            // Load saved theme preference
            IsDarkMode = _configService.CurrentConfig.IsDarkMode;
            ThemeService.ApplyTheme(IsDarkMode);

            // Wire cross-VM events
            RecordsVM.RequestEditRecord += OnRequestEditRecord;
            SettingsVM.DatabasePathChanged += OnDatabasePathChanged;
            SettingsVM.OpCoListChanged += OnOpCoListChanged;
            SettingsVM.DefectListChanged += OnDefectListChanged;
        }

        [RelayCommand]
        public void ToggleTheme()
        {
            IsDarkMode = !IsDarkMode;
            ThemeService.ApplyTheme(IsDarkMode);
            var config = _configService.CurrentConfig;
            config.IsDarkMode = IsDarkMode;
            _configService.SaveConfig(config);
        }

        private async void OnOpCoListChanged()
        {
            await EntryVM.LoadOpCoOptionsAsync();
            await RecordsVM.LoadOpCoFiltersAsync();
            await StatisticsVM.LoadFilterOptionsAsync();
        }

        private async void OnDefectListChanged()
        {
            await EntryVM.LoadDefectOptionsAsync();
            await RecordsVM.LoadDefectFiltersAsync();
            await StatisticsVM.LoadFilterOptionsAsync();
        }

        partial void OnSelectedTabIndexChanged(int value)
        {
            CurrentViewModel = value switch
            {
                0 => EntryVM,
                1 => RecordsVM,
                2 => StatisticsVM,
                3 => BulkImportVM,
                4 => SettingsVM,
                _ => EntryVM
            };

            if (value == 1)
            {
                _ = RecordsVM.SearchAsync();
            }
            else if (value == 2)
            {
                _ = StatisticsVM.RefreshStatisticsAsync();
            }
        }

        public async Task InitializeAsync()
        {
            await CheckDatabaseConnectionAsync();
            await EntryVM.InitializeAsync();
            await RecordsVM.InitializeAsync();
            await StatisticsVM.InitializeAsync();
            await RefreshStatsAsync();
        }

        public async Task CheckDatabaseConnectionAsync()
        {
            var (success, msg) = await _databaseService.TestConnectionAsync();
            IsDatabaseConnected = success;
            ConnectionStatusText = success ? "Connected" : "Disconnected";
            DatabasePath = _databaseService.DatabasePath;
        }

        public async Task RefreshStatsAsync()
        {
            try
            {
                var (total, today, pending) = await _repository.GetStatisticsAsync();
                TotalDatabaseRecords = total;
                TodayRecordsCount = today;
                PendingRecordsCount = pending;
            }
            catch
            {
                // Ignore stats load failure on startup
            }
        }

        private void OnRequestEditRecord(DeviceRecord record)
        {
            EntryVM.LoadRecordForEdit(record);
            SelectedTabIndex = 0; // Switch to Entry tab
        }

        private async void OnDatabasePathChanged()
        {
            await CheckDatabaseConnectionAsync();
            await EntryVM.InitializeAsync();
            await RecordsVM.InitializeAsync();
            await RefreshStatsAsync();
        }

        [RelayCommand]
        public async Task RefreshAllAsync()
        {
            await CheckDatabaseConnectionAsync();
            if (SelectedTabIndex == 0)
            {
                await EntryVM.RefreshRecentEntriesAsync();
            }
            else if (SelectedTabIndex == 1)
            {
                await RecordsVM.SearchAsync();
            }
            await RefreshStatsAsync();
        }

        [RelayCommand]
        public void NavigateTo(object? param)
        {
            if (param is int tabIndex)
            {
                SelectedTabIndex = tabIndex;
            }
            else if (param != null && int.TryParse(param.ToString(), out int parsedIndex))
            {
                SelectedTabIndex = parsedIndex;
            }
        }
    }
}
