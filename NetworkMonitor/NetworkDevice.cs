using System.Net;

namespace NetworkMonitor;

internal sealed class NetworkDevice
{
    public const string UnknownHostName = "Неизвестное устройство";

    public required string IpAddress { get; init; }
    public string HostName { get; set; } = UnknownHostName;
    public string MacAddress { get; set; } = "";
    public bool IsOnline { get; set; }
    public bool IsServer { get; set; }
    public bool NameResponded { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.Now;
    public string Source { get; set; } = "Автосканирование";
    public string OpenPorts { get; set; } = "";

    public string TypeText => IsServer ? "Сервер" : "Устройство";
    public string StatusText => IsOnline ? "В сети" : "Недоступен";
    public string CheckedAtText => CheckedAt.ToString("HH:mm:ss");
    public bool HasKnownHostName => IsKnownHostName(HostName);

    public static NetworkDevice Offline(IPAddress ipAddress, string source)
    {
        return new NetworkDevice
        {
            IpAddress = ipAddress.ToString(),
            HostName = UnknownHostName,
            IsOnline = false,
            Source = source,
            CheckedAt = DateTime.Now
        };
    }

    public static string MergeHostNames(params string[] values)
    {
        var names = values
            .SelectMany(SplitHostNames)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Where(IsKnownHostName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count == 0 ? UnknownHostName : string.Join("; ", names);
    }

    public static bool IsKnownHostName(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !value.Equals(UnknownHostName, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitHostNames(string value)
    {
        return value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Trim());
    }
}
