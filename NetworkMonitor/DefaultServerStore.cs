using System.Reflection;
using System.Text.Json;

namespace NetworkMonitor;

internal sealed class DefaultServerStore
{
    private const string DefaultServersFileName = "default_servers.json";
    private const string EmbeddedDefaultServersResourceName = "NetworkMonitor.default_servers.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;

    public DefaultServerStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetworkMonitor");

        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, DefaultServersFileName);
    }

    public List<DefaultServerEndpoint> Load()
    {
        EnsureDefaultServersFile();
        return LoadServersFromFile(_filePath);
    }

    public void Save(IEnumerable<DefaultServerEndpoint> servers)
    {
        var values = servers
            .Where(server => !string.IsNullOrWhiteSpace(server.Name))
            .Select(server => new SavedDefaultServer(server.Name.Trim(), server.Address.Trim()))
            .ToList();

        var json = JsonSerializer.Serialize(values, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private void EnsureDefaultServersFile()
    {
        if (File.Exists(_filePath))
        {
            return;
        }

        var applicationDefaultFilePath = Path.Combine(AppContext.BaseDirectory, DefaultServersFileName);
        if (File.Exists(applicationDefaultFilePath))
        {
            File.Copy(applicationDefaultFilePath, _filePath, overwrite: false);
            return;
        }

        File.WriteAllText(_filePath, ReadEmbeddedDefaultServersJson());
    }

    private static List<DefaultServerEndpoint> LoadServersFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            var values = JsonSerializer.Deserialize<List<SavedDefaultServer>>(File.ReadAllText(filePath), JsonOptions) ?? [];
            var servers = new List<DefaultServerEndpoint>();

            foreach (var value in values)
            {
                var name = value.Name.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var server = new DefaultServerEndpoint
                {
                    Name = name,
                    Address = value.Address?.Trim() ?? ""
                };

                var existingIndex = servers.FindIndex(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    servers[existingIndex] = server;
                }
                else
                {
                    servers.Add(server);
                }
            }

            return servers;
        }
        catch
        {
            return [];
        }
    }

    private static string ReadEmbeddedDefaultServersJson()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedDefaultServersResourceName);
        if (stream is null)
        {
            return "[]";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record SavedDefaultServer(string Name, string Address);
}
