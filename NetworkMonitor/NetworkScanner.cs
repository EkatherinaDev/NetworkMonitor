using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace NetworkMonitor;

internal sealed partial class NetworkScanner
{
    private const int PortTimeoutMilliseconds = 300;
    private const int RdpPortTimeoutMilliseconds = 1000;
    private const int PingNameTimeoutMilliseconds = 3000;
    private const int NameQueryTimeoutMilliseconds = 1000;
    private const int NbtStatTimeoutMilliseconds = 3000;
    private const int RdpCertificateTimeoutMilliseconds = 4000;
    private const int MaxAddressProbeParallelism = 32;
    private const int MaxEnrichmentParallelism = 48;
    private const int MaxSubnetAddresses = 1024;
    private const int RdpPort = 3389;
    private const uint RdpProtocolSsl = 0x00000001;
    private const uint RdpProtocolHybrid = 0x00000002;
    private const uint RdpProtocolHybridEx = 0x00000008;
    private const uint RdpRequestedProtocols = RdpProtocolSsl | RdpProtocolHybrid | RdpProtocolHybridEx;

    private static readonly int[] ServerPorts = [22, 25, 53, 80, 110, 143, 389, 443, 465, 587, 636, 993, 995, 1433, 1521, 3306, RdpPort, 5432, 8080, 8443];
    private static readonly string[] ServerNameTokens = ["server", "srv", "dc", "sql", "db", "1c", "ksc", "mail", "exchange", "nas", "storage", "backup", "terminal", "rdp", "web", "app"];
    private static readonly byte[] RdpTlsNegotiationRequest =
    [
        0x03, 0x00, 0x00, 0x13,
        0x0e, 0xe0, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x08, 0x00,
        (byte)(RdpRequestedProtocols & 0xFF),
        (byte)((RdpRequestedProtocols >> 8) & 0xFF),
        (byte)((RdpRequestedProtocols >> 16) & 0xFF),
        (byte)((RdpRequestedProtocols >> 24) & 0xFF)
    ];

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

        var arpCache = await ReadArpCacheAsync(cancellationToken);
        var devices = new ConcurrentBag<NetworkDevice>();
        var completedCount = 0;

        progress?.Report(new ScanProgress("Поиск IP по открытым портам...", 0, scanTargets.Count));

        await Parallel.ForEachAsync(
            scanTargets,
            new ParallelOptions { MaxDegreeOfParallelism = MaxAddressProbeParallelism, CancellationToken = cancellationToken },
            async (address, token) =>
            {
                var addressText = address.ToString();
                var source = manualAddressSet.Contains(addressText) ? "Вручную" : "Автосканирование";
                var device = await ProbeDeviceAsync(
                    address,
                    source,
                    arpCache,
                    includeUnavailable: manualAddressSet.Contains(addressText),
                    token);

                if (device is not null)
                {
                    devices.Add(device);
                }

                var completed = Interlocked.Increment(ref completedCount);
                progress?.Report(new ScanProgress($"Проверено IP: {completed} из {scanTargets.Count}", completed, scanTargets.Count));
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
        CancellationToken cancellationToken,
        bool forceServer = true)
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

        progress?.Report(new ScanProgress(forceServer ? "Проверка серверов..." : "Проверка IP...", 0, scanTargets.Count));

        await Parallel.ForEachAsync(
            scanTargets,
            new ParallelOptions { MaxDegreeOfParallelism = MaxEnrichmentParallelism, CancellationToken = cancellationToken },
            async (address, token) =>
            {
                var device = await BuildCheckedDeviceAsync(address, source, arpCache, token);
                if (forceServer)
                {
                    device.IsServer = true;
                }

                devices.Add(device);

                var completed = Interlocked.Increment(ref completedCount);
                var label = forceServer ? "серверов" : "IP";
                progress?.Report(new ScanProgress($"Проверено {label}: {completed} из {scanTargets.Count}", completed, scanTargets.Count));
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
        var arpCache = await ReadArpCacheAsync(cancellationToken);
        var device = await BuildCheckedDeviceAsync(address, source, arpCache, cancellationToken);
        if (requireServerNameResponse)
        {
            device.IsServer = true;
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

    private static async Task<NetworkDevice?> ProbeDeviceAsync(
        IPAddress address,
        string source,
        IReadOnlyDictionary<string, string> arpCache,
        bool includeUnavailable,
        CancellationToken cancellationToken)
    {
        var openPorts = await FindOpenServerPortsAsync(address, cancellationToken);
        if (openPorts.Count == 0 && !includeUnavailable)
        {
            return null;
        }

        return await BuildCheckedDeviceAsync(address, source, arpCache, cancellationToken, openPorts);
    }

    private static async Task<NetworkDevice> BuildCheckedDeviceAsync(
        IPAddress address,
        string source,
        IReadOnlyDictionary<string, string> arpCache,
        CancellationToken cancellationToken,
        IReadOnlyList<int>? checkedOpenPorts = null)
    {
        var openPorts = checkedOpenPorts ?? await FindOpenServerPortsAsync(address, cancellationToken);
        var directHostName = openPorts.Contains(RdpPort)
            ? await QueryRdpCertificateNameAsync(address, cancellationToken)
            : "";
        if (string.IsNullOrWhiteSpace(directHostName))
        {
            directHostName = await QueryNetBiosNameAsync(address, cancellationToken);
        }

        var hostName = !string.IsNullOrWhiteSpace(directHostName)
            ? directHostName
            : await ResolveHostNameAsync(address, cancellationToken);

        var isServer = LooksLikeServer(hostName, openPorts);
        var ipText = address.ToString();
        var nameResponded = !string.IsNullOrWhiteSpace(directHostName);
        var isOnline = openPorts.Contains(RdpPort);

        return new NetworkDevice
        {
            IpAddress = ipText,
            HostName = string.IsNullOrWhiteSpace(hostName) ? NetworkDevice.UnknownHostName : hostName,
            MacAddress = arpCache.TryGetValue(ipText, out var macAddress) ? macAddress : "",
            IsOnline = isOnline,
            IsServer = isServer,
            NameResponded = nameResponded,
            CheckedAt = DateTime.Now,
            Source = isOnline
                ? source
                : openPorts.Count == 0
                    ? $"{source} (нет открытых портов)"
                    : $"{source} (порт 3389 закрыт)",
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
            return NetworkDevice.UnknownHostName;
        }
    }

    private static string SimplifyHostName(string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return NetworkDevice.UnknownHostName;
        }

        var firstDot = hostName.IndexOf('.');
        return firstDot > 0 ? hostName[..firstDot] : hostName;
    }

    private static async Task<string> QueryRdpCertificateNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        TcpClient? client = null;
        X509Certificate2? remoteCertificate = null;

        try
        {
            using var timeout = new CancellationTokenSource(RdpCertificateTimeoutMilliseconds);
            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            client = new TcpClient(address.AddressFamily);
            var connectTask = client.ConnectAsync(address, RdpPort);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(RdpCertificateTimeoutMilliseconds, linkedToken.Token));
            if (completedTask != connectTask)
            {
                return "";
            }

            await connectTask;

            var stream = client.GetStream();
            await stream.WriteAsync(RdpTlsNegotiationRequest, linkedToken.Token);
            await stream.FlushAsync(linkedToken.Token);

            var negotiationResponse = await ReadRdpNegotiationResponseAsync(stream, linkedToken.Token);
            if (negotiationResponse is null || !IsRdpTlsNegotiationAccepted(negotiationResponse))
            {
                return "";
            }

            using var sslStream = new SslStream(
                stream,
                leaveInnerStreamOpen: false,
                (_, certificate, _, _) =>
                {
                    if (certificate is not null)
                    {
                        remoteCertificate = new X509Certificate2(certificate);
                    }

                    return true;
                });

            var options = new SslClientAuthenticationOptions
            {
                TargetHost = address.ToString(),
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            };

            await sslStream.AuthenticateAsClientAsync(options, linkedToken.Token);

            if (remoteCertificate is null && sslStream.RemoteCertificate is not null)
            {
                remoteCertificate = new X509Certificate2(sslStream.RemoteCertificate);
            }

            if (remoteCertificate is null)
            {
                return "";
            }

            using (remoteCertificate)
            {
                return ExtractCertificateHostName(remoteCertificate);
            }
        }
        catch
        {
            remoteCertificate?.Dispose();
            return "";
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static async Task<byte[]?> ReadRdpNegotiationResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(stream, 4, cancellationToken);
        if (header is null || header[0] != 0x03)
        {
            return null;
        }

        var packetLength = (header[2] << 8) | header[3];
        if (packetLength < 4 || packetLength > 4096)
        {
            return null;
        }

        var response = new byte[packetLength];
        Buffer.BlockCopy(header, 0, response, 0, header.Length);

        var body = await ReadExactAsync(stream, packetLength - header.Length, cancellationToken);
        if (body is null)
        {
            return null;
        }

        Buffer.BlockCopy(body, 0, response, header.Length, body.Length);
        return response;
    }

    private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int byteCount, CancellationToken cancellationToken)
    {
        var buffer = new byte[byteCount];
        var offset = 0;

        while (offset < byteCount)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(offset, byteCount - offset), cancellationToken);
            if (bytesRead == 0)
            {
                return null;
            }

            offset += bytesRead;
        }

        return buffer;
    }

    private static bool IsRdpTlsNegotiationAccepted(byte[] response)
    {
        for (var offset = 0; offset <= response.Length - 8; offset++)
        {
            var type = response[offset];
            var structureLength = response[offset + 2] | (response[offset + 3] << 8);
            if (structureLength != 8)
            {
                continue;
            }

            if (type == 0x03)
            {
                return false;
            }

            if (type != 0x02)
            {
                continue;
            }

            var selectedProtocol = (uint)(response[offset + 4]
                | (response[offset + 5] << 8)
                | (response[offset + 6] << 16)
                | (response[offset + 7] << 24));

            return (selectedProtocol & RdpRequestedProtocols) != 0;
        }

        return false;
    }

    private static string ExtractCertificateHostName(X509Certificate2 certificate)
    {
        var dnsName = NormalizeCertificateHostName(certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false));
        if (!string.IsNullOrWhiteSpace(dnsName))
        {
            return dnsName;
        }

        var simpleName = NormalizeCertificateHostName(certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
        if (!string.IsNullOrWhiteSpace(simpleName))
        {
            return simpleName;
        }

        var commonNameMatch = Regex.Match(certificate.Subject, @"(?:^|,\s*)CN\s*=\s*(?<name>[^,]+)", RegexOptions.IgnoreCase);
        return commonNameMatch.Success
            ? NormalizeCertificateHostName(commonNameMatch.Groups["name"].Value)
            : "";
    }

    private static string NormalizeCertificateHostName(string value)
    {
        var name = value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        if (name.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            name = name[3..].Trim();
        }

        if (name.StartsWith("TERMSRV/", StringComparison.OrdinalIgnoreCase))
        {
            name = name["TERMSRV/".Length..].Trim();
        }

        if (name.StartsWith("*.", StringComparison.OrdinalIgnoreCase))
        {
            name = name[2..].Trim();
        }

        if (IPAddress.TryParse(name, out _))
        {
            return "";
        }

        var hostName = SimplifyHostName(name);
        return hostName == NetworkDevice.UnknownHostName ? "" : hostName;
    }

    private static async Task<string> QueryNetBiosNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        var pingName = await QueryPingNameAsync(address, cancellationToken);
        if (!string.IsNullOrWhiteSpace(pingName))
        {
            return pingName;
        }

        var nbtStatName = await QueryNbtStatNameAsync(address, cancellationToken);
        if (!string.IsNullOrWhiteSpace(nbtStatName))
        {
            return nbtStatName;
        }

        return await QueryBuiltInNetBiosNameAsync(address, cancellationToken);
    }

    private static async Task<string> QueryPingNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        Process? process = null;

        try
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ping.exe",
                    Arguments = $"-a -n 1 -w 1000 {address}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            if (!process.Start())
            {
                return "";
            }

            using var timeout = new CancellationTokenSource(PingNameTimeoutMilliseconds);
            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var outputTask = process.StandardOutput.ReadToEndAsync(linkedToken.Token);
            var errorTask = process.StandardError.ReadToEndAsync(linkedToken.Token);

            await process.WaitForExitAsync(linkedToken.Token);

            var output = await outputTask;
            var error = await errorTask;
            return ParsePingNameOutput($"{output}{Environment.NewLine}{error}", address);
        }
        catch
        {
            TryKillProcess(process);
            return "";
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static string ParsePingNameOutput(string output, IPAddress address)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "";
        }

        var addressMarker = $"[{address}]";
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var markerIndex = rawLine.IndexOf(addressMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex <= 0)
            {
                continue;
            }

            var prefix = rawLine[..markerIndex].Trim();
            var name = prefix
                .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();

            if (string.IsNullOrWhiteSpace(name) || name.Equals(address.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return SimplifyHostName(name);
        }

        return "";
    }

    private static async Task<string> QueryNbtStatNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        Process? process = null;

        try
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nbtstat.exe",
                    Arguments = $"-A {address}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            if (!process.Start())
            {
                return "";
            }

            using var timeout = new CancellationTokenSource(NbtStatTimeoutMilliseconds);
            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var outputTask = process.StandardOutput.ReadToEndAsync(linkedToken.Token);
            var errorTask = process.StandardError.ReadToEndAsync(linkedToken.Token);

            await process.WaitForExitAsync(linkedToken.Token);

            var output = await outputTask;
            var error = await errorTask;
            return ParseNbtStatOutput($"{output}{Environment.NewLine}{error}");
        }
        catch
        {
            TryKillProcess(process);
            return "";
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static string ParseNbtStatOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "";
        }

        var serverName = "";
        var workstationName = "";
        var fallbackName = "";

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryReadNbtStatName(rawLine, out var name, out var suffix, out var isGroupName))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(fallbackName))
            {
                fallbackName = name;
            }

            if (isGroupName)
            {
                continue;
            }

            if (suffix.Equals("20", StringComparison.OrdinalIgnoreCase))
            {
                serverName = name;
            }
            else if (suffix.Equals("00", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(workstationName))
            {
                workstationName = name;
            }
        }

        if (!string.IsNullOrWhiteSpace(serverName))
        {
            return serverName;
        }

        return !string.IsNullOrWhiteSpace(workstationName) ? workstationName : fallbackName;
    }

    private static bool TryReadNbtStatName(string line, out string name, out string suffix, out bool isGroupName)
    {
        name = "";
        suffix = "";
        isGroupName = false;

        var suffixStart = line.IndexOf('<');
        var suffixEnd = suffixStart >= 0 ? line.IndexOf('>', suffixStart + 1) : -1;
        if (suffixStart <= 0 || suffixEnd <= suffixStart + 1)
        {
            return false;
        }

        suffix = line.Substring(suffixStart + 1, suffixEnd - suffixStart - 1).Trim();
        if (suffix.Length != 2 || suffix.Any(character => !Uri.IsHexDigit(character)))
        {
            return false;
        }

        name = line[..suffixStart].Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Equals("__MSBROWSE__", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        isGroupName = line.Contains("GROUP", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ГРУП", StringComparison.OrdinalIgnoreCase);

        return true;
    }

    private static void TryKillProcess(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore cleanup failures after a timeout or cancelled name lookup.
        }
    }

    private static async Task<string> QueryBuiltInNetBiosNameAsync(IPAddress address, CancellationToken cancellationToken)
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
            var timeoutMilliseconds = port == RdpPort ? RdpPortTimeoutMilliseconds : PortTimeoutMilliseconds;
            var timeoutTask = Task.Delay(timeoutMilliseconds, cancellationToken);
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
        if (openPorts.Count > 0)
        {
            return true;
        }

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
