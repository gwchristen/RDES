using System;
using System.Threading;
using System.Threading.Tasks;
using RDES.App.Models;

namespace RDES.App.Services
{
    public interface IModemCommunicator
    {
        ModemState CurrentState { get; }
        string PortName { get; }
        bool IsConnected { get; }

        event EventHandler<ModemState>? StateChanged;
        event EventHandler<string>? LogMessageOccurred;

        Task<bool> ConnectAsync(string portName, CancellationToken ct = default);
        Task DisconnectAsync();
        Task<string> SendCommandAsync(string command, CancellationToken ct = default);
        Task<bool> PingAsync(CancellationToken ct = default);

        // Simulation methods for USB disconnect/reconnect testing
        void SimulateUsbDisconnect();
        void SimulateUsbReconnect();
    }
}
