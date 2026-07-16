namespace NetworkMonitor;

internal sealed class DefaultServerEndpoint
{
    public required string Name { get; set; }
    public string Address { get; set; } = "";
    public string PingStatus { get; set; } = "Не проверено";
    public string RdpStatus { get; set; } = "Не проверено";
    public string DetectedNames { get; set; } = "";
    public string MacAddresses { get; set; } = "";
    public string OpenPorts { get; set; } = "";
    public bool IsOnline { get; set; }
    public DateTime? CheckedAt { get; set; }
    public string Details { get; set; } = "Не проверено";
    public IReadOnlyList<NetworkDevice> LastDevices { get; set; } = [];
    public IReadOnlyDictionary<string, bool> LastPingResults { get; set; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    public string StatusText => IsOnline ? "В сети" : "Недоступен";
    public string CheckedAtText => CheckedAt?.ToString("HH:mm:ss") ?? "";
}
