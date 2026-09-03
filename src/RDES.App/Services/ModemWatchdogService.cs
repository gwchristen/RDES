using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class ModemWatchdogService
    {
        private readonly IModemCommunicator _communicator;
        private readonly ModemRecoveryService _recoveryService;
        private readonly IncidentLogService _incidentLogService;

        private readonly ConcurrentQueue<DateTime> _failureTimestamps = new();
        private readonly DateTime _startTime = DateTime.Now;

        private int _totalRetries = 0;
        private int _totalCommandsExecuted = 0;
        private int _totalCommandFailures = 0;
        private DateTime? _lastConnectedAt = DateTime.Now;
        private DateTime? _lastDisconnectedAt;
        private TimeSpan _totalDisconnectedTime = TimeSpan.Zero;
        private DateTime? _currentDisconnectStartTime;

        private CancellationTokenSource? _watchdogCts;
        private bool _isRunning = false;

        public ModemHealthMetrics CurrentMetrics { get; private set; } = new();
        public event EventHandler<ModemHealthMetrics>? MetricsUpdated;

        public ModemWatchdogService(
            IModemCommunicator communicator,
            ModemRecoveryService recoveryService,
            IncidentLogService incidentLogService)
        {
            _communicator = communicator;
            _recoveryService = recoveryService;
            _incidentLogService = incidentLogService;

            _communicator.StateChanged += OnStateChanged;
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _watchdogCts = new CancellationTokenSource();
            _ = RunWatchdogLoopAsync(_watchdogCts.Token);
        }

        public void Stop()
        {
            _isRunning = false;
            _watchdogCts?.Cancel();
        }

        public void RecordRetry()
        {
            Interlocked.Increment(ref _totalRetries);
            UpdateMetrics();
        }

        public void RecordCommandExecution(bool success)
        {
            Interlocked.Increment(ref _totalCommandsExecuted);
            if (!success)
            {
                Interlocked.Increment(ref _totalCommandFailures);
                RecordFailure();
            }
            UpdateMetrics();
        }

        public void RecordFailure()
        {
            _failureTimestamps.Enqueue(DateTime.Now);
            UpdateMetrics();
        }

        private void OnStateChanged(object? sender, ModemState state)
        {
            if (state == ModemState.Connected)
            {
                _lastConnectedAt = DateTime.Now;
                if (_currentDisconnectStartTime.HasValue)
                {
                    _totalDisconnectedTime += DateTime.Now - _currentDisconnectStartTime.Value;
                    _currentDisconnectStartTime = null;
                }
            }
            else if (state == ModemState.Disconnected || state == ModemState.Reconnecting || state == ModemState.Failed)
            {
                _lastDisconnectedAt = DateTime.Now;
                if (!_currentDisconnectStartTime.HasValue)
                {
                    _currentDisconnectStartTime = DateTime.Now;
                }
            }
            UpdateMetrics();
        }

        private async Task RunWatchdogLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    UpdateMetrics();

                    // If connected, perform lightweight heartbeat check
                    if (_communicator.IsConnected && _communicator.CurrentState == ModemState.Connected)
                    {
                        bool healthy = await _communicator.PingAsync(ct);
                        if (!healthy)
                        {
                            RecordFailure();
                            await _incidentLogService.LogIncidentAsync(
                                "Warning",
                                "HeartbeatFailed",
                                $"Watchdog heartbeat check failed on port {_communicator.PortName}.",
                                _communicator.PortName);

                            _communicator.SimulateUsbDisconnect();
                        }
                    }

                    await Task.Delay(2000, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Watchdog loop error: {ex.Message}");
                }
            }
        }

        public ModemHealthMetrics UpdateMetrics()
        {
            // Prune failure timestamps older than 1 hour
            DateTime oneHourAgo = DateTime.Now.AddHours(-1);
            while (_failureTimestamps.TryPeek(out var timestamp) && timestamp < oneHourAgo)
            {
                _failureTimestamps.TryDequeue(out _);
            }

            double failuresPerHour = _failureTimestamps.Count;

            // Calculate uptime
            TimeSpan totalSpan = DateTime.Now - _startTime;
            TimeSpan currentDowntime = _currentDisconnectStartTime.HasValue
                ? (DateTime.Now - _currentDisconnectStartTime.Value)
                : TimeSpan.Zero;
            TimeSpan totalDowntime = _totalDisconnectedTime + currentDowntime;

            double uptime = 100.0;
            if (totalSpan.TotalSeconds > 0)
            {
                double onlineSeconds = Math.Max(0, totalSpan.TotalSeconds - totalDowntime.TotalSeconds);
                uptime = Math.Min(100.0, Math.Max(0.0, (onlineSeconds / totalSpan.TotalSeconds) * 100.0));
            }

            CurrentMetrics = new ModemHealthMetrics
            {
                State = _communicator.CurrentState,
                TotalRetries = _totalRetries,
                TotalDisconnects = _recoveryService.TotalDisconnects,
                TotalRecoveries = _recoveryService.TotalRecoveries,
                FailuresPerHour = failuresPerHour,
                UptimePercentage = uptime,
                LastConnectedAt = _lastConnectedAt,
                LastDisconnectedAt = _lastDisconnectedAt,
                TotalCommandsExecuted = _totalCommandsExecuted,
                TotalCommandFailures = _totalCommandFailures,
                ActivePortName = _communicator.PortName
            };

            MetricsUpdated?.Invoke(this, CurrentMetrics);
            return CurrentMetrics;
        }
    }
}
