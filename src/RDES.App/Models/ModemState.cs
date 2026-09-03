namespace RDES.App.Models
{
    public enum ModemState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Failed,
        Degraded,
        SoakTesting
    }
}
