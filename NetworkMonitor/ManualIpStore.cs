using System.Net;
using System.Text.Json;

namespace NetworkMonitor;

internal sealed class ManualIpStore
{
    private readonly string _filePath;

    public ManualIpStore(string fileName = "manual_ips.json")
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetworkMonitor");

        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, fileName);
    }

    public List<string> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var values = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_filePath)) ?? [];
            return values
                .Select(NormalizeOrNull)
                .Where(value => value is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(IpSortKey)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<string> values)
    {
        var normalizedValues = values
            .Select(NormalizeOrNull)
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(IpSortKey)
            .ToList();

        var json = JsonSerializer.Serialize(normalizedValues, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    public static bool TryNormalize(string value, out string normalized)
    {
        normalized = "";
        if (!IPAddress.TryParse(value.Trim(), out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        normalized = address.ToString();
        return true;
    }

    private static string? NormalizeOrNull(string value)
    {
        return TryNormalize(value, out var normalized) ? normalized : null;
    }

    private static uint IpSortKey(string value)
    {
        return IPAddress.TryParse(value, out var address) ? NetworkScanner.ToSortableUInt32(address) : 0;
    }
}
