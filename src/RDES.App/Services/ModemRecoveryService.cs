using System;
using System.Threading;
using System.Threading.Tasks;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class ModemRecoveryService
    {
        private readonly IModemCommunicator _communicator;
        private readonly ExponentialBackoffPolicy _backoffPolicy;
        private readonly IncidentLogService _incidentLogService;

        private CancellationTokenSource? _recoveryCts;
        private bool _isRecovering = false;
        private int _totalRecoveries = 0;
        private int _totalDisconnects = 0;

        public bool IsRecovering => _isRecovering;
        public int TotalRecoveries => _totalRecoveries;
        public int TotalDisconnects => _totalDisconnects;

        public event EventHandler<ModemState>? StateChanged;

        public ModemRecoveryService(
            IModemCommunicator communicator,
            ExponentialBackoffPolicy backoffPolicy,
            IncidentLogService incidentLogService)
        {
            _communicator = communicator;
            _backoffPolicy = backoffPolicy;
            _incidentLogService = incidentLogService;

            _communicator.StateChanged += OnCommunicatorStateChanged;
        }

        private async void OnCommunicatorStateChanged(object? sender, ModemState newState)
        {
            StateChanged?.Invoke(this, newState);

            if (newState == ModemState.Disconnected && !_isRecovering)
            {
                _totalDisconnects++;
                await _incidentLogService.LogIncidentAsync(
                    "Warning",
                    "Disconnect",
                    $"USB Disconnect detected on modem port {_communicator.PortName}. Initiating auto-recovery.",
                    _communicator.PortName);

                _ = StartAutoRecoveryAsync();
            }
        }

        public async Task<bool> StartAutoRecoveryAsync(CancellationToken externalCt = default)
        {
            if (_isRecovering) return false;

            _isRecovering = true;
            _recoveryCts?.Cancel();
            _recoveryCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            var ct = _recoveryCts.Token;

            try
            {
                StateChanged?.Invoke(this, ModemState.Reconnecting);

                bool reconnected = await _backoffPolicy.ExecuteWithRetryAsync<bool>(
                    async token =>
                    {
                        token.ThrowIfCancellationRequested();
                        bool success = await _communicator.ConnectAsync(_communicator.PortName, token);
                        if (!success)
                        {
                            throw new InvalidOperationException("Reconnection attempt failed.");
                        }
                        return true;
                    },
                    onRetry: async (ex, attempt) =>
                    {
                        await _incidentLogService.LogIncidentAsync(
                            "Info",
                            "RetryAttempt",
                            $"Auto-recovery attempt #{attempt} for port {_communicator.PortName} failed: {ex.Message}",
                            _communicator.PortName,
                            ex);
                    },
                    parentToken: ct);

                if (reconnected && _communicator.IsConnected)
                {
                    _totalRecoveries++;
                    await _incidentLogService.LogIncidentAsync(
                        "Info",
                        "Reconnect",
                        $"Modem auto-recovery succeeded on port {_communicator.PortName}. Total recoveries: {_totalRecoveries}.",
                        _communicator.PortName);

                    StateChanged?.Invoke(this, ModemState.Connected);
                    return true;
                }
            }
            catch (Exception ex)
            {
                await _incidentLogService.LogIncidentAsync(
                    "Error",
                    "MaxRetriesExceeded",
                    $"Modem auto-recovery failed on port {_communicator.PortName}: {ex.Message}",
                    _communicator.PortName,
                    ex);

                StateChanged?.Invoke(this, ModemState.Failed);
            }
            finally
            {
                _isRecovering = false;
            }

            return false;
        }

        public void CancelRecovery()
        {
            _recoveryCts?.Cancel();
            _isRecovering = false;
        }
    }
}
