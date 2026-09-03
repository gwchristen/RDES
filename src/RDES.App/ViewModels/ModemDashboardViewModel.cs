using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDES.App.Models;
using RDES.App.Services;

namespace RDES.App.ViewModels
{
    public partial class ModemDashboardViewModel : ObservableObject
    {
        private readonly IModemCommunicator _communicator;
        private readonly ExponentialBackoffPolicy _backoffPolicy;
        private readonly ModemRecoveryService _recoveryService;
        private readonly ModemWatchdogService _watchdogService;
        private readonly ModemSoakTestService _soakTestService;
        private readonly BatchSessionService _batchService;
        private readonly IncidentLogService _incidentLogService;

        // Modem Health Properties
        [ObservableProperty]
        private ModemState _currentState = ModemState.Disconnected;

        [ObservableProperty]
        private string _portName = "COM3";

        [ObservableProperty]
        private int _totalRetries;

        [ObservableProperty]
        private int _totalDisconnects;

        [ObservableProperty]
        private int _totalRecoveries;

        [ObservableProperty]
        private double _failuresPerHour;

        [ObservableProperty]
        private double _uptimePercentage = 100.0;

        [ObservableProperty]
        private string _lastConnectedText = "Never";

        [ObservableProperty]
        private string _lastDisconnectedText = "Never";

        [ObservableProperty]
        private string _statusMessage = "Modem Initialized";

        // Soak Mode Properties
        [ObservableProperty]
        private double _targetSoakHours = 8.0;

        [ObservableProperty]
        private int _healthCheckIntervalSeconds = 15;

        [ObservableProperty]
        private bool _stopSoakOnFailure = false;

        [ObservableProperty]
        private bool _isSoakRunning = false;

        [ObservableProperty]
        private string _soakStatusText = "Idle";

        [ObservableProperty]
        private int _soakTotalChecks;

        [ObservableProperty]
        private int _soakPassedChecks;

        [ObservableProperty]
        private int _soakFailedChecks;

        [ObservableProperty]
        private string _soakElapsedTimeText = "00:00:00";

        // Batch Sessions Properties
        [ObservableProperty]
        private string _newBatchName = "Batch_RMA_01";

        [ObservableProperty]
        private string _newBatchSerialsText = "SN-A1001\nSN-A1002\nSN-A1003\nSN-A1004\nSN-A1005";

        [ObservableProperty]
        private BatchSession? _selectedBatchSession;

        [ObservableProperty]
        private string _batchStatusText = "Ready";

        [ObservableProperty]
        private ObservableCollection<BatchSession> _batchSessions = new();

        // Incident Logs Properties
        [ObservableProperty]
        private ObservableCollection<ModemIncidentLog> _incidentLogs = new();

        [ObservableProperty]
        private string _selectedSeverityFilter = "All";

        [ObservableProperty]
        private string _exportDiagnosticsStatus = string.Empty;

        public ModemDashboardViewModel(
            IModemCommunicator communicator,
            ExponentialBackoffPolicy backoffPolicy,
            ModemRecoveryService recoveryService,
            ModemWatchdogService watchdogService,
            ModemSoakTestService soakTestService,
            BatchSessionService batchService,
            IncidentLogService incidentLogService)
        {
            _communicator = communicator;
            _backoffPolicy = backoffPolicy;
            _recoveryService = recoveryService;
            _watchdogService = watchdogService;
            _soakTestService = soakTestService;
            _batchService = batchService;
            _incidentLogService = incidentLogService;

            _watchdogService.MetricsUpdated += OnMetricsUpdated;
            _soakTestService.StatusChanged += OnSoakStatusChanged;

            _watchdogService.Start();
        }

        public async Task InitializeAsync()
        {
            await RefreshBatchSessionsAsync();
            await RefreshLogsAsync();

            // Default connection
            if (!_communicator.IsConnected)
            {
                await _communicator.ConnectAsync(PortName);
            }
        }

        private void OnMetricsUpdated(object? sender, ModemHealthMetrics metrics)
        {
            CurrentState = metrics.State;
            PortName = metrics.ActivePortName;
            TotalRetries = metrics.TotalRetries;
            TotalDisconnects = metrics.TotalDisconnects;
            TotalRecoveries = metrics.TotalRecoveries;
            FailuresPerHour = metrics.FailuresPerHour;
            UptimePercentage = metrics.UptimePercentage;
            LastConnectedText = metrics.LastConnectedAt.HasValue ? metrics.LastConnectedAt.Value.ToString("HH:mm:ss") : "Never";
            LastDisconnectedText = metrics.LastDisconnectedAt.HasValue ? metrics.LastDisconnectedAt.Value.ToString("HH:mm:ss") : "Never";
        }

        private void OnSoakStatusChanged(object? sender, SoakTestStatus status)
        {
            IsSoakRunning = status.IsRunning;
            SoakStatusText = status.StatusMessage;
            SoakTotalChecks = status.TotalChecks;
            SoakPassedChecks = status.PassedChecks;
            SoakFailedChecks = status.FailedChecks;
            SoakElapsedTimeText = status.ElapsedTime.ToString(@"hh\:mm\:ss");
        }

        [RelayCommand]
        public async Task ConnectModemAsync()
        {
            await _communicator.ConnectAsync(PortName);
            StatusMessage = $"Connected on port {PortName}.";
        }

        [RelayCommand]
        public async Task DisconnectModemAsync()
        {
            await _communicator.DisconnectAsync();
            StatusMessage = "Modem disconnected.";
        }

        [RelayCommand]
        public void SimulateUsbDisconnect()
        {
            _communicator.SimulateUsbDisconnect();
            StatusMessage = "Simulated USB disconnect triggered.";
        }

        [RelayCommand]
        public void SimulateUsbReconnect()
        {
            _communicator.SimulateUsbReconnect();
            StatusMessage = "Simulated USB reconnect triggered.";
        }

        [RelayCommand]
        public async Task TriggerAutoRecoveryAsync()
        {
            StatusMessage = "Starting auto-recovery loop...";
            bool result = await _recoveryService.StartAutoRecoveryAsync();
            StatusMessage = result ? "Auto-recovery succeeded!" : "Auto-recovery failed.";
        }

        [RelayCommand]
        public async Task StartSoakTestAsync()
        {
            var config = new SoakTestConfig
            {
                TargetDurationHours = TargetSoakHours,
                HealthCheckIntervalSeconds = HealthCheckIntervalSeconds,
                StopOnFailure = StopSoakOnFailure
            };

            bool started = await _soakTestService.StartSoakTestAsync(config);
            StatusMessage = started ? $"Soak mode started ({TargetSoakHours:F1}h)." : "Failed to start soak mode.";
        }

        [RelayCommand]
        public void StopSoakTest()
        {
            _soakTestService.StopSoakTest();
            StatusMessage = "Soak mode stopped.";
        }

        [RelayCommand]
        public async Task CreateBatchSessionAsync()
        {
            var serials = NewBatchSerialsText
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (serials.Count == 0)
            {
                BatchStatusText = "No serial numbers entered.";
                return;
            }

            var session = await _batchService.CreateBatchSessionAsync(NewBatchName, serials);
            BatchStatusText = $"Created session '{session.Name}' ({session.TotalItems} items).";
            await RefreshBatchSessionsAsync();
        }

        [RelayCommand]
        public async Task ResumeBatchSessionAsync()
        {
            if (SelectedBatchSession == null)
            {
                BatchStatusText = "Please select a batch session to resume.";
                return;
            }

            string sessionId = SelectedBatchSession.Id;
            BatchStatusText = $"Resuming batch session '{SelectedBatchSession.Name}'...";

            bool completed = await _batchService.ResumeBatchSessionAsync(
                sessionId,
                async item =>
                {
                    // Execute item via modem with retry policy
                    return await _backoffPolicy.ExecuteWithRetryAsync<bool>(async ct =>
                    {
                        string response = await _communicator.SendCommandAsync($"READ_BARCODE:{item.SerialNumber}", ct);
                        _watchdogService.RecordCommandExecution(true);
                        return response.Contains("OK");
                    },
                    onRetry: async (ex, attempt) =>
                    {
                        _watchdogService.RecordRetry();
                    });
                });

            BatchStatusText = completed ? "Batch session completed!" : "Batch session paused/failed.";
            await RefreshBatchSessionsAsync();
        }

        [RelayCommand]
        public async Task RefreshBatchSessionsAsync()
        {
            var list = await _batchService.GetAllBatchSessionsAsync();
            BatchSessions = new ObservableCollection<BatchSession>(list);
        }

        [RelayCommand]
        public async Task RefreshLogsAsync()
        {
            string? severity = SelectedSeverityFilter == "All" ? null : SelectedSeverityFilter;
            var logs = await _incidentLogService.GetIncidentLogsAsync(100, severity);
            IncidentLogs = new ObservableCollection<ModemIncidentLog>(logs);
        }

        [RelayCommand]
        public async Task ExportDiagnosticsAsync()
        {
            string exportDir = Path.Combine(ConfigService.GetAppDirectory(), "Diagnostics");
            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }

            string filePath = Path.Combine(exportDir, $"RunDiagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            bool success = await _incidentLogService.ExportDiagnosticsAsync(filePath, "json", _watchdogService.CurrentMetrics, _soakTestService.Status);
            ExportDiagnosticsStatus = success ? $"✅ Diagnostics exported to: {filePath}" : "❌ Failed to export diagnostics.";
        }
    }
}
