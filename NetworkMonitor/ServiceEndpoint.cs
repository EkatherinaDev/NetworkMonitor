namespace NetworkMonitor;

internal sealed class ServiceEndpoint
{
    public required string Name { get; set; }
    public string Address { get; set; } = "";
    public string ResolvedIp { get; set; } = "";
    public bool IsOnline { get; set; }
    public DateTime? CheckedAt { get; set; }
    public string Details { get; set; } = "Не проверено";

    public string StatusText => IsOnline ? "В сети" : "Недоступен";
    public string CheckedAtText => CheckedAt?.ToString("HH:mm:ss") ?? "";
}
