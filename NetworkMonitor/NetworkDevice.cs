using System.Net;

namespace NetworkMonitor;

internal sealed class NetworkDevice
{
    public required string IpAddress { get; init; }
    public string HostName { get; set; } = "Неизвестное устройство";
    public string MacAddress { get; set; } = "";
    public bool IsOnline { get; set; }
    public bool IsServer { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.Now;
    public string Source { get; set; } = "Автосканирование";
    public string OpenPorts { get; set; } = "";

    public string TypeText => IsServer ? "Сервер" : "Устройство";
    public string StatusText => IsOnline ? "В сети" : "Недоступен";
    public string CheckedAtText => CheckedAt.ToString("HH:mm:ss");

    public static NetworkDevice Offline(IPAddress ipAddress, string source)
    {
        return new NetworkDevice
        {
            IpAddress = ipAddress.ToString(),
            HostName = "Неизвестное устройство",
            IsOnline = false,
            Source = source,
            CheckedAt = DateTime.Now
        };
    }
}
