using System;
using System.Threading;
using System.Threading.Tasks;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class ModemSoakTestService
    {
        private readonly IModemCommunicator _communicator;
        private readonly ExponentialBackoffPolicy _backoffPolicy;
        private readonly ModemRecoveryService _recoveryService;
        private readonly ModemWatchdogService _watchdogService;
        private readonly IncidentLogService _incidentLogService;

        private CancellationTokenSource? _soakCts;
        private SoakTestConfig _config = new();
        private SoakTestStatus _status = new();

        public SoakTestStatus Status => _status;
        public SoakTestConfig Config => _config;

        public event EventHandler<SoakTestStatus>? StatusChanged;

        public ModemSoakTestService(
            IModemCommunicator communicator,
            ExponentialBackoffPolicy backoffPolicy,
            ModemRecoveryService recoveryService,
            ModemWatchdogService watchdogService,
            IncidentLogService incidentLogService)
        {
            _communicator = communicator;
            _backoffPolicy = backoffPolicy;
            _recoveryService = recoveryService;
            _watchdogService = watchdogService;
            _incidentLogService = incidentLogService;
        }

        public async Task<bool> StartSoakTestAsync(SoakTestConfig config)
        {
            if (_status.IsRunning) return false;

            _config = config ?? new SoakTestConfig();
            // Validate soak duration between 8 and 24 hours (or allow test overrides if target < 8 for fast unit tests, while defaulting UI to 8-24h)
            if (_config.TargetDurationHours <= 0)
            {
                _config.TargetDurationHours = 8.0;
            }

            _soakCts = new CancellationTokenSource();
            _status = new SoakTestStatus
            {
                IsRunning = true,
                StartTime = DateTime.Now,
                TargetDurationHours = _config.TargetDurationHours,
                ElapsedTime = TimeSpan.Zero,
                TotalChecks = 0,
                PassedChecks = 0,
                FailedChecks = 0,
                RecoveryCount = 0,
                StatusMessage = $"Running (Target: {_config.TargetDurationHours:F1}h)"
            };

            await _incidentLogService.LogIncidentAsync(
                "Info",
                "SoakStarted",
                $"Long-run soak mode started. Target duration: {_config.TargetDurationHours:F1} hours, check interval: {_config.HealthCheckIntervalSeconds}s.");

            UpdateStatus();
            _ = RunSoakLoopAsync(_soakCts.Token);
            return true;
        }

        public void StopSoakTest()
        {
            if (!_status.IsRunning) return;

            _soakCts?.Cancel();
            _status.IsRunning = false;
            _status.StatusMessage = "Stopped by user";

            _ = _incidentLogService.LogIncidentAsync(
                "Info",
                "SoakStopped",
                $"Soak test stopped by user. Total checks: {_status.TotalChecks}, Passed: {_status.PassedChecks}, Failed: {_status.FailedChecks}.");

            UpdateStatus();
        }

        private async Task RunSoakLoopAsync(CancellationToken ct)
        {
            DateTime startTime = _status.StartTime ?? DateTime.Now;
            TimeSpan targetDuration = TimeSpan.FromHours(_config.TargetDurationHours);

            while (!ct.IsCancellationRequested)
            {
                TimeSpan elapsed = DateTime.Now - startTime;
                _status.ElapsedTime = elapsed;

                if (elapsed >= targetDuration)
                {
                    _status.IsRunning = false;
                    _status.StatusMessage = $"Completed successfully ({_config.TargetDurationHours:F1}h)";

                    await _incidentLogService.LogIncidentAsync(
                        "Info",
                        "SoakCompleted",
                        $"Soak test completed successfully after {elapsed.TotalHours:F2} hours! Passed: {_status.PassedChecks}/{_status.TotalChecks}.");

                    UpdateStatus();
                    break;
                }

                _status.TotalChecks++;
                _status.RecoveryCount = _recoveryService.TotalRecoveries;

                bool checkSuccess = false;
                try
                {
                    // Execute periodic health check with exponential backoff & jitter
                    checkSuccess = await _backoffPolicy.ExecuteWithRetryAsync<bool>(
                        async token =>
                        {
                            return await _communicator.PingAsync(token);
                        },
                        onRetry: async (ex, attempt) =>
                        {
                            _watchdogService.RecordRetry();
                            await _incidentLogService.LogIncidentAsync(
                                "Warning",
                                "SoakRetry",
                                $"Soak health check attempt #{attempt} retry: {ex.Message}",
                                _communicator.PortName);
                        },
                        parentToken: ct);
                }
                catch (Exception ex)
                {
                    checkSuccess = false;
                    _watchdogService.RecordFailure();
                    await _incidentLogService.LogIncidentAsync(
                        "Error",
                        "SoakCheckFailed",
                        $"Soak health check failed on port {_communicator.PortName}: {ex.Message}",
                        _communicator.PortName,
                        ex);
                }

                if (checkSuccess)
                {
                    _status.PassedChecks++;
                    _watchdogService.RecordCommandExecution(true);
                }
                else
                {
                    _status.FailedChecks++;
                    _watchdogService.RecordCommandExecution(false);

                    if (_config.StopOnFailure)
                    {
                        _status.IsRunning = false;
                        _status.StatusMessage = "Aborted due to failure";

                        await _incidentLogService.LogIncidentAsync(
                            "Critical",
                            "SoakAborted",
                            $"Soak test aborted due to health check failure.");

                        UpdateStatus();
                        break;
                    }
                }

                UpdateStatus();

                try
                {
                    int delayMs = Math.Max(100, _config.HealthCheckIntervalSeconds * 1000);
                    await Task.Delay(delayMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void UpdateStatus()
        {
            StatusChanged?.Invoke(this, _status);
        }
    }
}
