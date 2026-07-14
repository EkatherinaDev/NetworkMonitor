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
    private const int NameQueryTimeoutMilliseconds = 1000;
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

    public async Task<NetworkScanResult> CheckAddressesAsync(
        IEnumerable<string> ipAddresses,
        string source,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var scanTargets = ipAddresses
            .Where(value => ManualIpStore.TryNormalize(value, out _))
            .Select(IPAddress.Parse)
            .DistinctBy(address => address.ToString())
            .OrderBy(ToSortableUInt32)
            .ToList();

        var scannedAddressSet = scanTargets.Select(address => address.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (scanTargets.Count == 0)
        {
            return new NetworkScanResult([], scannedAddressSet, new HashSet<string>(StringComparer.OrdinalIgnoreCase), DateTime.Now);
        }

        var arpCache = await ReadArpCacheAsync(cancellationToken);
        var devices = new ConcurrentBag<NetworkDevice>();
        var completedCount = 0;

        progress?.Report(new ScanProgress("Проверка серверов...", 0, scanTargets.Count));

        await Parallel.ForEachAsync(
            scanTargets,
            new ParallelOptions { MaxDegreeOfParallelism = MaxEnrichmentParallelism, CancellationToken = cancellationToken },
            async (address, token) =>
            {
                var device = await CheckServerIpAsync(address, source, arpCache, token);

                devices.Add(device);

                var completed = Interlocked.Increment(ref completedCount);
                progress?.Report(new ScanProgress($"Проверено серверов: {completed} из {scanTargets.Count}", completed, scanTargets.Count));
            });

        return new NetworkScanResult(
            devices.OrderByDescending(device => device.IsServer).ThenBy(device => ToSortableUInt32(IPAddress.Parse(device.IpAddress))).ToList(),
            scannedAddressSet,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            DateTime.Now);
    }

    public async Task<NetworkDevice> CheckIpAsync(IPAddress address, string source, CancellationToken cancellationToken)
    {
        return await CheckIpAsync(address, source, requireServerNameResponse: false, cancellationToken);
    }

    public async Task<NetworkDevice> CheckIpAsync(
        IPAddress address,
        string source,
        bool requireServerNameResponse,
        CancellationToken cancellationToken)
    {
        if (!await PingAsync(address, cancellationToken))
        {
            return NetworkDevice.Offline(address, source);
        }

        var arpCache = await ReadArpCacheAsync(cancellationToken);
        var device = await BuildOnlineDeviceAsync(address, source, arpCache, cancellationToken);
        if (requireServerNameResponse && !HasServerResponse(device))
        {
            device.IsOnline = false;
            device.IsServer = true;
            device.Source = $"{source} (нет ответа имени/портов)";
        }

        return device;
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
        var directHostName = await QueryNetBiosNameAsync(address, cancellationToken);
        var hostName = !string.IsNullOrWhiteSpace(directHostName)
            ? directHostName
            : await ResolveHostNameAsync(address, cancellationToken);

        var openPorts = await FindOpenServerPortsAsync(address, cancellationToken);
        var isServer = LooksLikeServer(hostName, openPorts);
        var ipText = address.ToString();
        var nameResponded = !string.IsNullOrWhiteSpace(directHostName);
        var hasServerResponse = nameResponded || openPorts.Count > 0;

        return new NetworkDevice
        {
            IpAddress = ipText,
            HostName = string.IsNullOrWhiteSpace(hostName) ? "Неизвестное устройство" : hostName,
            MacAddress = arpCache.TryGetValue(ipText, out var macAddress) ? macAddress : "",
            IsOnline = !isServer || hasServerResponse,
            IsServer = isServer,
            NameResponded = nameResponded,
            CheckedAt = DateTime.Now,
            Source = isServer && !hasServerResponse ? $"{source} (нет ответа имени/портов)" : source,
            OpenPorts = openPorts.Count == 0 ? "" : string.Join(", ", openPorts)
        };
    }

    private static async Task<NetworkDevice> CheckServerIpAsync(
        IPAddress address,
        string source,
        IReadOnlyDictionary<string, string> arpCache,
        CancellationToken cancellationToken)
    {
        if (!await PingAsync(address, cancellationToken))
        {
            var offlineDevice = NetworkDevice.Offline(address, source);
            offlineDevice.IsServer = true;
            return offlineDevice;
        }

        var device = await BuildOnlineDeviceAsync(address, source, arpCache, cancellationToken);
        device.IsServer = true;
        if (!HasServerResponse(device))
        {
            device.IsOnline = false;
            device.Source = $"{source} (нет ответа имени/портов)";
        }

        return device;
    }

    private static bool HasServerResponse(NetworkDevice device)
    {
        return device.NameResponded || !string.IsNullOrWhiteSpace(device.OpenPorts);
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

    private static async Task<string> QueryNetBiosNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.ReceiveTimeout = NameQueryTimeoutMilliseconds;
            client.Connect(address, 137);

            var query = CreateNetBiosNodeStatusQuery();
            await client.SendAsync(query, query.Length).WaitAsync(cancellationToken);

            using var timeout = new CancellationTokenSource(NameQueryTimeoutMilliseconds);
            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var result = await client.ReceiveAsync(linkedToken.Token);

            return ParseNetBiosNodeStatusResponse(result.Buffer);
        }
        catch
        {
            return "";
        }
    }

    private static byte[] CreateNetBiosNodeStatusQuery()
    {
        var query = new byte[50];
        var transactionId = Random.Shared.Next(0, ushort.MaxValue + 1);

        query[0] = (byte)(transactionId >> 8);
        query[1] = (byte)(transactionId & 0xFF);
        query[5] = 1;
        query[12] = 32;

        var wildcardName = "*               ";
        var offset = 13;
        foreach (var value in wildcardName.Select(character => (byte)character))
        {
            query[offset++] = (byte)('A' + ((value >> 4) & 0x0F));
            query[offset++] = (byte)('A' + (value & 0x0F));
        }

        query[offset++] = 0;
        query[offset++] = 0;
        query[offset++] = 0x21;
        query[offset++] = 0;
        query[offset] = 1;

        return query;
    }

    private static string ParseNetBiosNodeStatusResponse(byte[] response)
    {
        if (response.Length < 57)
        {
            return "";
        }

        var answerCount = (response[6] << 8) | response[7];
        if (answerCount == 0)
        {
            return "";
        }

        var offset = SkipDnsName(response, 12);
        if (offset < 0 || offset + 4 > response.Length)
        {
            return "";
        }

        offset += 4;
        for (var answerIndex = 0; answerIndex < answerCount; answerIndex++)
        {
            offset = SkipDnsName(response, offset);
            if (offset < 0 || offset + 10 > response.Length)
            {
                return "";
            }

            var type = (response[offset] << 8) | response[offset + 1];
            var dataLength = (response[offset + 8] << 8) | response[offset + 9];
            offset += 10;

            if (offset + dataLength > response.Length)
            {
                return "";
            }

            if (type == 0x21 && dataLength > 1)
            {
                return ParseNetBiosNames(response, offset, dataLength);
            }

            offset += dataLength;
        }

        return "";
    }

    private static int SkipDnsName(byte[] buffer, int offset)
    {
        while (offset < buffer.Length)
        {
            var length = buffer[offset++];
            if (length == 0)
            {
                return offset;
            }

            if ((length & 0xC0) == 0xC0)
            {
                return offset + 1 <= buffer.Length ? offset + 1 : -1;
            }

            offset += length;
        }

        return -1;
    }

    private static string ParseNetBiosNames(byte[] response, int offset, int dataLength)
    {
        var endOffset = offset + dataLength;
        var nameCount = response[offset++];
        var namesEndOffset = offset + (nameCount * 18);
        if (namesEndOffset > endOffset)
        {
            return "";
        }

        var fallbackName = "";
        for (var index = 0; index < nameCount; index++)
        {
            var name = System.Text.Encoding.ASCII.GetString(response, offset, 15).Trim();
            var suffix = response[offset + 15];
            var flags = (response[offset + 16] << 8) | response[offset + 17];
            var isGroupName = (flags & 0x8000) != 0;
            offset += 18;

            if (string.IsNullOrWhiteSpace(name) || name == "__MSBROWSE__")
            {
                continue;
            }

            fallbackName = string.IsNullOrWhiteSpace(fallbackName) ? name : fallbackName;

            if (!isGroupName && suffix is 0x00 or 0x20)
            {
                return name;
            }
        }

        return fallbackName;
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
