using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkMonitor;

internal sealed class ServiceChecker
{
    private const int DnsTimeoutMilliseconds = 2500;
    private const int PingTimeoutMilliseconds = 1200;
    private const int MaxParallelism = 8;
    private static readonly char[] TargetSeparators = [';', ',', '\r', '\n', '\t'];

    public async Task<IReadOnlyList<ServiceEndpoint>> CheckAsync(
        IEnumerable<ServiceEndpoint> services,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var targets = services.ToList();
        var results = new ServiceEndpoint[targets.Count];
        var completed = 0;

        progress?.Report(new ScanProgress("Проверка сервисов...", 0, Math.Max(targets.Count, 1)));

        await Parallel.ForEachAsync(
            targets.Select((service, index) => (Service: service, Index: index)),
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
            async (item, token) =>
            {
                results[item.Index] = await CheckOneAsync(item.Service, token);
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new ScanProgress($"Проверено сервисов: {done} из {targets.Count}", done, Math.Max(targets.Count, 1)));
            });

        return results;
    }

    private static async Task<ServiceEndpoint> CheckOneAsync(ServiceEndpoint service, CancellationToken cancellationToken)
    {
        var result = new ServiceEndpoint
        {
            Name = service.Name,
            Address = service.Address.Trim(),
            CheckedAt = DateTime.Now
        };

        var candidates = SplitTargets(result.Address);
        if (candidates.Count == 0)
        {
            result.Details = "Укажите DNS-имя или IP";
            return result;
        }

        ServiceTargetAttempt? firstResolvedAttempt = null;
        foreach (var candidate in candidates)
        {
            var resolvedIp = await ResolveIPv4Async(candidate, cancellationToken);
            if (resolvedIp is null)
            {
                continue;
            }

            var isOnline = await PingAsync(resolvedIp, cancellationToken);
            var attempt = new ServiceTargetAttempt(candidate, resolvedIp, isOnline);
            firstResolvedAttempt ??= attempt;

            if (!isOnline)
            {
                continue;
            }

            result.ResolvedIp = resolvedIp.ToString();
            result.IsOnline = true;
            result.Details = candidates.Count == 1 ? "OK" : $"OK: {candidate}";
            return result;
        }

        if (firstResolvedAttempt is not null)
        {
            result.ResolvedIp = firstResolvedAttempt.IpAddress.ToString();
            result.Details = candidates.Count == 1
                ? "Нет ответа ping"
                : $"Нет ответа ping: {firstResolvedAttempt.Target}";
            return result;
        }

        result.Details = candidates.Count == 1 ? "IPv4 не найден" : "IPv4 не найден ни для одного имени";
        return result;
    }

    private static IReadOnlyList<string> SplitTargets(string targets)
    {
        return targets
            .Split(TargetSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<IPAddress?> ResolveIPv4Async(string address, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(address, out var ipAddress))
        {
            return ipAddress.AddressFamily == AddressFamily.InterNetwork ? ipAddress : null;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(address)
                .WaitAsync(TimeSpan.FromMilliseconds(DnsTimeoutMilliseconds), cancellationToken);

            return addresses.FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> PingAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, PingTimeoutMilliseconds).WaitAsync(cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private sealed record ServiceTargetAttempt(string Target, IPAddress IpAddress, bool IsOnline);
}
