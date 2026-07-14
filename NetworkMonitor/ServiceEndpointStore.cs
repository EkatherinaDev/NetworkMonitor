using System.Reflection;
using System.Text.Json;

namespace NetworkMonitor;

internal sealed class ServiceEndpointStore
{
    private const string ServicesFileName = "service_endpoints.json";
    private const string DefaultServicesFileName = "default_services.json";
    private const string EmbeddedDefaultServicesResourceName = "NetworkMonitor.default_services.json";
    private static readonly char[] AddressSeparators = [';', ',', '\r', '\n', '\t'];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly string _defaultFilePath;

    public ServiceEndpointStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetworkMonitor");

        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, ServicesFileName);
        _defaultFilePath = Path.Combine(directory, DefaultServicesFileName);
    }

    public List<ServiceEndpoint> Load()
    {
        var defaults = LoadDefaultServices();
        if (File.Exists(_filePath))
        {
            var savedServices = LoadServicesFromFile(_filePath);
            var mergedServices = MergeSavedServicesWithDefaults(savedServices, defaults, out var changed);

            if (changed)
            {
                Save(mergedServices);
            }

            return mergedServices;
        }

        Save(defaults);
        return defaults;
    }

    public void Save(IEnumerable<ServiceEndpoint> services)
    {
        var values = services
            .Select(service => new SavedServiceEndpoint(service.Name, service.Address.Trim()))
            .ToList();

        var json = JsonSerializer.Serialize(values, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private List<ServiceEndpoint> LoadDefaultServices()
    {
        EnsureDefaultServicesFile();
        var defaults = LoadServicesFromFile(_defaultFilePath);
        return defaults.Count > 0 ? defaults : LoadEmbeddedDefaultServices();
    }

    private List<ServiceEndpoint> LoadServicesFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            var values = JsonSerializer.Deserialize<List<SavedServiceEndpoint>>(File.ReadAllText(filePath), JsonOptions) ?? [];
            var services = new List<ServiceEndpoint>();

            foreach (var value in values)
            {
                var name = value.Name.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var existingIndex = services.FindIndex(service => service.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                var service = new ServiceEndpoint
                {
                    Name = name,
                    Address = value.Address?.Trim() ?? ""
                };

                if (existingIndex >= 0)
                {
                    services[existingIndex] = service;
                }
                else
                {
                    services.Add(service);
                }
            }

            return services;
        }
        catch
        {
            return [];
        }
    }

    private List<ServiceEndpoint> MergeSavedServicesWithDefaults(
        List<ServiceEndpoint> savedServices,
        List<ServiceEndpoint> defaultServices,
        out bool changed)
    {
        changed = false;

        foreach (var defaultService in defaultServices)
        {
            var existingService = savedServices.FirstOrDefault(service => IsSameDefaultService(service.Name, defaultService.Name));
            if (existingService is null)
            {
                savedServices.Add(Clone(defaultService));
                changed = true;
                continue;
            }

            if (!existingService.Name.Equals(defaultService.Name, StringComparison.Ordinal))
            {
                existingService.Name = defaultService.Name;
                changed = true;
            }

            if (ShouldApplyDefaultAddress(existingService.Address, defaultService.Address))
            {
                existingService.Address = defaultService.Address;
                changed = true;
            }
        }

        return savedServices;
    }

    private void EnsureDefaultServicesFile()
    {
        if (File.Exists(_defaultFilePath))
        {
            return;
        }

        var applicationDefaultFilePath = Path.Combine(AppContext.BaseDirectory, DefaultServicesFileName);
        if (File.Exists(applicationDefaultFilePath))
        {
            File.Copy(applicationDefaultFilePath, _defaultFilePath, overwrite: true);
            return;
        }

        var json = ReadEmbeddedDefaultServicesJson();
        File.WriteAllText(_defaultFilePath, json);
    }

    private static List<ServiceEndpoint> LoadEmbeddedDefaultServices()
    {
        try
        {
            return JsonSerializer.Deserialize<List<SavedServiceEndpoint>>(ReadEmbeddedDefaultServicesJson(), JsonOptions)?
                .Where(service => !string.IsNullOrWhiteSpace(service.Name))
                .Select(service => new ServiceEndpoint
                {
                    Name = service.Name.Trim(),
                    Address = service.Address?.Trim() ?? ""
                })
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string ReadEmbeddedDefaultServicesJson()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedDefaultServicesResourceName);
        if (stream is null)
        {
            return "[]";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static bool IsSameDefaultService(string serviceName, string defaultServiceName)
    {
        return serviceName.Equals(defaultServiceName, StringComparison.OrdinalIgnoreCase)
            || (defaultServiceName.Equals("Covid19", StringComparison.OrdinalIgnoreCase)
                && serviceName.Equals("Covid", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldApplyDefaultAddress(string currentAddress, string defaultAddress)
    {
        if (string.IsNullOrWhiteSpace(defaultAddress))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(currentAddress))
        {
            return true;
        }

        var currentCandidates = SplitAddressCandidates(currentAddress);
        var defaultCandidates = SplitAddressCandidates(defaultAddress);

        return currentCandidates.Count > 0
            && currentCandidates.Count < defaultCandidates.Count
            && currentCandidates.All(defaultCandidates.Contains);
    }

    private static HashSet<string> SplitAddressCandidates(string address)
    {
        return address
            .Split(AddressSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static ServiceEndpoint Clone(ServiceEndpoint service)
    {
        return new ServiceEndpoint
        {
            Name = service.Name,
            Address = service.Address
        };
    }

    private sealed record SavedServiceEndpoint(string Name, string Address);
}
