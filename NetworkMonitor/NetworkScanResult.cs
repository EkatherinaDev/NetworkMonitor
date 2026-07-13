namespace NetworkMonitor;

internal sealed record NetworkScanResult(
    IReadOnlyList<NetworkDevice> Devices,
    IReadOnlySet<string> ScannedAddresses,
    IReadOnlySet<string> ManualAddresses,
    DateTime CompletedAt);

internal sealed record ScanProgress(string Message, int Completed, int Total);
