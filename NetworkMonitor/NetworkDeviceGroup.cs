using System.Net;

namespace NetworkMonitor;

internal sealed class NetworkDeviceGroup
{
    private NetworkDeviceGroup(IReadOnlyList<NetworkDevice> devices)
    {
        Devices = devices;
        HostName = NetworkDevice.MergeHostNames(devices.Select(device => device.HostName).ToArray());
        IpAddresses = JoinIpAddresses(devices);
        MacAddresses = JoinDistinct(devices.Select(device => device.MacAddress));
        OpenPorts = JoinOpenPorts(devices.Select(device => device.OpenPorts));
        Source = JoinDistinct(devices.Select(device => device.Source));
        IsOnline = devices.Any(device => device.IsOnline);
        IsServer = devices.Any(device => device.IsServer);
        CheckedAt = devices.Max(device => device.CheckedAt);
    }

    public IReadOnlyList<NetworkDevice> Devices { get; }
    public string HostName { get; }
    public string IpAddresses { get; }
    public string MacAddresses { get; }
    public string OpenPorts { get; }
    public string Source { get; }
    public bool IsOnline { get; }
    public bool IsServer { get; }
    public DateTime CheckedAt { get; }

    public string TypeText => IsServer ? "Сервер" : "Устройство";
    public string StatusText => IsOnline ? "В сети" : "Недоступен";
    public string CheckedAtText => CheckedAt.ToString("HH:mm:ss");
    public NetworkDevice PrimaryDevice => Devices[0];

    public static NetworkDeviceGroup Create(IEnumerable<NetworkDevice> devices)
    {
        var orderedDevices = devices
            .OrderBy(device => NetworkScanner.ToSortableUInt32(IPAddress.Parse(device.IpAddress)))
            .ToList();

        return new NetworkDeviceGroup(orderedDevices);
    }

    private static string JoinIpAddresses(IEnumerable<NetworkDevice> devices)
    {
        return string.Join("; ", devices.Select(device => device.IpAddress));
    }

    private static string JoinDistinct(IEnumerable<string> values)
    {
        return string.Join("; ", values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string JoinOpenPorts(IEnumerable<string> values)
    {
        var ports = values
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => int.TryParse(value, out var port) ? port : int.MaxValue)
            .ThenBy(value => value, StringComparer.OrdinalIgnoreCase);

        return string.Join(", ", ports);
    }
}
