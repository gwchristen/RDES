using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class ModemCommunicator : IModemCommunicator
    {
        private ModemState _currentState = ModemState.Disconnected;
        private string _portName = "COM3";
        private bool _isConnected = false;
        private bool _isSimulatedDisconnected = false;

        public ModemState CurrentState => _currentState;
        public string PortName => _portName;
        public bool IsConnected => _isConnected && !_isSimulatedDisconnected;

        public event EventHandler<ModemState>? StateChanged;
        public event EventHandler<string>? LogMessageOccurred;

        public Task<bool> ConnectAsync(string portName, CancellationToken ct = default)
        {
            _portName = string.IsNullOrWhiteSpace(portName) ? "COM3" : portName;
            _isSimulatedDisconnected = false;
            _isConnected = true;

            SetState(ModemState.Connected);
            Log($"Connected to modem on port {_portName}.");
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            _isConnected = false;
            SetState(ModemState.Disconnected);
            Log($"Disconnected from modem on port {_portName}.");
            return Task.CompletedTask;
        }

        public async Task<string> SendCommandAsync(string command, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                throw new IOException($"Modem on port {_portName} is disconnected.");
            }

            ct.ThrowIfCancellationRequested();
            await Task.Delay(50, ct); // Simulate I/O latency

            if (command == "AT+PING" || command == "AT")
            {
                return "OK";
            }
            else if (command.StartsWith("READ_BARCODE"))
            {
                return "OK:SN-MODEM-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
            }

            return $"OK:{command}";
        }

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            try
            {
                string response = await SendCommandAsync("AT", ct);
                return response.Contains("OK");
            }
            catch
            {
                return false;
            }
        }

        public void SimulateUsbDisconnect()
        {
            _isSimulatedDisconnected = true;
            _isConnected = false;
            SetState(ModemState.Disconnected);
            Log($"USB Disconnect simulated on port {_portName}.");
        }

        public void SimulateUsbReconnect()
        {
            _isSimulatedDisconnected = false;
            _isConnected = true;
            SetState(ModemState.Connected);
            Log($"USB Reconnect simulated on port {_portName}.");
        }

        private void SetState(ModemState newState)
        {
            if (_currentState != newState)
            {
                _currentState = newState;
                StateChanged?.Invoke(this, _currentState);
            }
        }

        private void Log(string message)
        {
            LogMessageOccurred?.Invoke(this, message);
        }
    }
}
