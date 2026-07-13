using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace NetworkMonitor;

internal sealed partial class NetworkScanner
{
    private const int PingTimeoutMilliseconds = 800;
    private const int PortTimeoutMilliseconds = 300;
    private const int MaxPingParallelism = 128;
    private const int MaxEnrichmentParallelism = 48;
    private const int MaxSubnetAddresses = 1024;

    private static readonly int[] ServerPorts = [22, 25, 53, 80, 110, 143, 389, 443, 465, 587, 636, 993, 995, 1433, 1521, 3306, 3389, 5432, 8080, 8443];
    private static readonly string[] ServerNameTokens = ["server", "srv", "dc", "sql", "db", "1c", "ksc", "mail", "exchange", "nas", "storage", "backup", "terminal", "rdp", "web", "app"];

    public async Task<NetworkScanResult> ScanLocalNetworksAsync(
        IEnumerable<string> manualIpAddresses,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var manualIps = manualIpAddresses
            .Where(value => ManualIpStore.TryNormalize(value, out _))
            .Select(IPAddress.Parse)
            .DistinctBy(address => address.ToString())
            .ToList();

        var scanTargets = DiscoverLocalScanTargets()
            .Concat(manualIps)
            .DistinctBy(address => address.ToString())
            .OrderBy(ToSortableUInt32)
            .ToList();

        var scannedAddressSet = scanTargets.Select(address => address.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manualAddressSet = manualIps.Select(address => address.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (scanTargets.Count == 0)
        {
            return new NetworkScanResult([], scannedAddressSet, manualAddressSet, DateTime.Now);
        }

        var onlineAddresses = new ConcurrentBag<IPAddress>();
        var pingCompleted = 0;

        progress?.Report(new ScanProgress("Проверка доступности IP...", 0, scanTargets.Count));

        await Parallel.ForEachAsync(
            scanTargets,
            new ParallelOptions { MaxDegreeOfParallelism = MaxPingParallelism, CancellationToken = cancellationToken },
            async (address, token) =>
            {
                if (await PingAsync(address, token))
                {
                    onlineAddresses.Add(address);
                }

                var completed = Interlocked.Increment(ref pingCompleted);
                progress?.Report(new ScanProgress($"Проверено IP: {completed} из {scanTargets.Count}", completed, scanTargets.Count));
            });

        var arpCache = await ReadArpCacheAsync(cancellationToken);
        var devices = new ConcurrentBag<NetworkDevice>();
        var onlineList = onlineAddresses.OrderBy(ToSortableUInt32).ToList();
        var enrichCompleted = 0;

        progress?.Report(new ScanProgress("Определение имен и серверов...", 0, Math.Max(onlineList.Count, 1)));

        await Parallel.ForEachAsync(
            onlineList,
            new ParallelOptions { MaxDegreeOfParallelism = MaxEnrichmentParallelism, CancellationToken = cancellationToken },
            async (address, token) =>
            {
                var source = manualAddressSet.Contains(address.ToString()) ? "Вручную" : "Автосканирование";
                devices.Add(await BuildOnlineDeviceAsync(address, source, arpCache, token));

                var completed = Interlocked.Increment(ref enrichCompleted);
                progress?.Report(new ScanProgress($"Обработано активных устройств: {completed} из {onlineList.Count}", completed, Math.Max(onlineList.Count, 1)));
            });

        return new NetworkScanResult(
            devices.OrderByDescending(device => device.IsServer).ThenBy(device => ToSortableUInt32(IPAddress.Parse(device.IpAddress))).ToList(),
            scannedAddressSet,
            manualAddressSet,
            DateTime.Now);
    }

    public async Task<NetworkDevice> CheckIpAsync(IPAddress address, string source, CancellationToken cancellationToken)
    {
        if (!await PingAsync(address, cancellationToken))
        {
            return NetworkDevice.Offline(address, source);
        }

        var arpCache = await ReadArpCacheAsync(cancellationToken);
        return await BuildOnlineDeviceAsync(address, source, arpCache, cancellationToken);
    }

    public static uint ToSortableUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return 0;
        }

        return ((uint)bytes[0] << 24)
            | ((uint)bytes[1] << 16)
            | ((uint)bytes[2] << 8)
            | bytes[3];
    }

    private static async Task<bool> PingAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, PingTimeoutMilliseconds).WaitAsync(cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<NetworkDevice> BuildOnlineDeviceAsync(
        IPAddress address,
        string source,
        IReadOnlyDictionary<string, string> arpCache,
        CancellationToken cancellationToken)
    {
        var hostName = await ResolveHostNameAsync(address, cancellationToken);
        var openPorts = await FindOpenServerPortsAsync(address, cancellationToken);
        var isServer = LooksLikeServer(hostName, openPorts);
        var ipText = address.ToString();

        return new NetworkDevice
        {
            IpAddress = ipText,
            HostName = string.IsNullOrWhiteSpace(hostName) ? "Неизвестное устройство" : hostName,
            MacAddress = arpCache.TryGetValue(ipText, out var macAddress) ? macAddress : "",
            IsOnline = true,
            IsServer = isServer,
            CheckedAt = DateTime.Now,
            Source = source,
            OpenPorts = openPorts.Count == 0 ? "" : string.Join(", ", openPorts)
        };
    }

    private static async Task<string> ResolveHostNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(address)
                .WaitAsync(TimeSpan.FromSeconds(1.5), cancellationToken);

            return SimplifyHostName(entry.HostName);
        }
        catch
        {
            return "Неизвестное устройство";
        }
    }

    private static string SimplifyHostName(string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return "Неизвестное устройство";
        }

        var firstDot = hostName.IndexOf('.');
        return firstDot > 0 ? hostName[..firstDot] : hostName;
    }

    private static async Task<IReadOnlyList<int>> FindOpenServerPortsAsync(IPAddress address, CancellationToken cancellationToken)
    {
        var tasks = ServerPorts.Select(port => IsPortOpenAsync(address, port, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks);

        return results
            .Where(result => result.IsOpen)
            .Select(result => result.Port)
            .Order()
            .ToList();
    }

    private static async Task<(int Port, bool IsOpen)> IsPortOpenAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient(address.AddressFamily);
            var connectTask = client.ConnectAsync(address, port);
            var timeoutTask = Task.Delay(PortTimeoutMilliseconds, cancellationToken);
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask != connectTask)
            {
                return (port, false);
            }

            await connectTask;
            return (port, client.Connected);
        }
        catch
        {
            return (port, false);
        }
    }

    private static bool LooksLikeServer(string hostName, IReadOnlyCollection<int> openPorts)
    {
        var normalizedName = hostName.ToLowerInvariant();
        if (ServerNameTokens.Any(token => normalizedName.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return openPorts.Any(port => port is 22 or 25 or 53 or 80 or 389 or 443 or 636 or 1433 or 1521 or 3306 or 3389 or 5432 or 8080 or 8443);
    }

    private static List<IPAddress> DiscoverLocalScanTargets()
    {
        var targets = new List<IPAddress>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var ipProperties = networkInterface.GetIPProperties();
            foreach (var unicast in ipProperties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                {
                    continue;
                }

                targets.AddRange(EnumerateSubnet(unicast.Address, unicast.IPv4Mask));
            }

            foreach (var gateway in ipProperties.GatewayAddresses)
            {
                if (gateway.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    targets.Add(gateway.Address);
                }
            }
        }

        return targets
            .Where(address => !IPAddress.IsLoopback(address))
            .DistinctBy(address => address.ToString())
            .ToList();
    }

    private static IEnumerable<IPAddress> EnumerateSubnet(IPAddress address, IPAddress mask)
    {
        var addressValue = ToSortableUInt32(address);
        var maskValue = ToSortableUInt32(mask);
        var network = addressValue & maskValue;
        var broadcast = network | ~maskValue;
        var usableCount = broadcast > network ? broadcast - network - 1 : 0;

        if (usableCount == 0)
        {
            yield break;
        }

        if (usableCount > MaxSubnetAddresses)
        {
            var classCNetwork = addressValue & 0xFFFFFF00;
            for (var host = 1u; host <= 254u; host++)
            {
                yield return FromSortableUInt32(classCNetwork | host);
            }

            yield break;
        }

        for (var current = network + 1; current < broadcast; current++)
        {
            yield return FromSortableUInt32(current);
        }
    }

    private static IPAddress FromSortableUInt32(uint value)
    {
        return new IPAddress([
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF)
        ]);
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadArpCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "arp.exe",
                Arguments = "-a",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return ParseArpOutput(output);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyDictionary<string, string> ParseArpOutput(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ArpLineRegex().Matches(output))
        {
            var ipAddress = match.Groups["ip"].Value;
            var macAddress = match.Groups["mac"].Value.Replace('-', ':').ToLowerInvariant();
            result[ipAddress] = macAddress;
        }

        return result;
    }

    [GeneratedRegex(@"(?<ip>(?:\d{1,3}\.){3}\d{1,3})\s+(?<mac>(?:[0-9a-fA-F]{2}[-:]){5}[0-9a-fA-F]{2})")]
    private static partial Regex ArpLineRegex();
}
