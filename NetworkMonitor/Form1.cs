using System.Net;

namespace NetworkMonitor;

public partial class Form1 : Form
{
    private const int MaxEventLogItems = 500;

    private readonly NetworkScanner _scanner = new();
    private readonly ManualIpStore _manualIpStore = new();
    private readonly ManualIpStore _serverIpStore = new("server_ips.json");
    private readonly Dictionary<string, NetworkDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _manualIpAddresses;
    private readonly List<string> _serverIpAddresses;
    private CancellationTokenSource? _scanCancellation;
    private bool _scanInProgress;
    private Font? _serverRowFont;

    public Form1()
    {
        _manualIpAddresses = _manualIpStore.Load();
        _serverIpAddresses = _serverIpStore.Load();
        InitializeComponent();
        ConfigureGrid();

        autoScanTimer.Interval = 10 * 60 * 1000;
        autoScanTimer.Tick += async (_, _) => await RunServerScanAsync("Автоматическая проверка серверов");
        autoScanTimer.Start();

        Shown += async (_, _) =>
        {
            AddEventLog("Приложение запущено. Автоматически проверяются только известные серверы.");
            await RunServerScanAsync("Первичная проверка серверов");
        };
        FormClosing += (_, _) => _scanCancellation?.Cancel();
    }

    private void ConfigureGrid()
    {
        _serverRowFont = new Font(devicesGrid.Font, FontStyle.Bold);

        devicesGrid.Columns.Clear();
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Тип", Width = 105 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Имя / описание", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ip", HeaderText = "IP-адрес", Width = 130 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mac", HeaderText = "MAC-адрес", Width = 150 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ports", HeaderText = "Открытые порты", Width = 145 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Статус", Width = 115 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CheckedAt", HeaderText = "Проверено", Width = 105 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Источник", Width = 135 });
    }

    private List<string> GetServerIpAddresses()
    {
        return _serverIpAddresses
            .Concat(_devices.Values.Where(device => device.IsServer).Select(device => device.IpAddress))
            .Where(value => ManualIpStore.TryNormalize(value, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => NetworkScanner.ToSortableUInt32(IPAddress.Parse(value)))
            .ToList();
    }

    private void RememberServerAddresses(IEnumerable<NetworkDevice> devices)
    {
        var changed = false;
        foreach (var address in devices
            .Where(device => device.IsServer)
            .Select(device => device.IpAddress)
            .Where(value => ManualIpStore.TryNormalize(value, out _)))
        {
            if (_serverIpAddresses.Contains(address, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _serverIpAddresses.Add(address);
            changed = true;
            AddEventLog($"Сервер добавлен в автопроверку: {address}.");
        }

        if (!changed)
        {
            return;
        }

        _serverIpStore.Save(_serverIpAddresses);
        _serverIpAddresses.Clear();
        _serverIpAddresses.AddRange(_serverIpStore.Load());
    }

    private void AddEventLog(string message)
    {
        if (eventLogListBox.Items.Count >= MaxEventLogItems)
        {
            eventLogListBox.Items.RemoveAt(0);
        }

        eventLogListBox.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        eventLogListBox.TopIndex = eventLogListBox.Items.Count - 1;
    }

    private bool IsKnownServerIp(string ipAddress)
    {
        return _serverIpAddresses.Contains(ipAddress, StringComparer.OrdinalIgnoreCase)
            || (_devices.TryGetValue(ipAddress, out var device) && device.IsServer);
    }

    private async Task RunServerScanAsync(string reason)
    {
        if (_scanInProgress)
        {
            return;
        }

        var serverIps = GetServerIpAddresses();
        if (serverIps.Count == 0)
        {
            statusLabel.Text = "Нет известных серверов для автоматической проверки. Используйте сканирование всей сети.";
            AddEventLog("Автопроверка пропущена: список серверов пуст.");
            return;
        }

        _scanInProgress = true;
        _scanCancellation?.Cancel();
        _scanCancellation = new CancellationTokenSource();
        SetScanningState(true, reason);
        AddEventLog($"{reason}: {serverIps.Count} IP.");

        try
        {
            var progress = new Progress<ScanProgress>(UpdateProgress);
            var result = await _scanner.CheckAddressesAsync(serverIps, "Автопроверка серверов", progress, _scanCancellation.Token);

            foreach (var device in result.Devices)
            {
                device.IsServer = true;
                UpsertDevice(device);
            }

            RenderDevices();
            lastUpdateLabel.Text = $"Последняя проверка серверов: {result.CompletedAt:HH:mm:ss}";

            var onlineCount = result.Devices.Count(device => device.IsOnline);
            var offlineCount = result.Devices.Count - onlineCount;
            statusLabel.Text = $"Проверка серверов завершена. В сети: {onlineCount}, недоступно: {offlineCount}";
            AddEventLog($"Проверка серверов завершена: в сети {onlineCount}, недоступно {offlineCount}.");
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Проверка серверов остановлена.";
            AddEventLog("Проверка серверов остановлена.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Ошибка проверки серверов.";
            AddEventLog($"Ошибка проверки серверов: {ex.Message}");
            MessageBox.Show($"Ошибка проверки серверов: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetScanningState(false, "");
            _scanInProgress = false;
        }
    }

    private async Task RunNetworkScanAsync(string reason)
    {
        if (_scanInProgress)
        {
            return;
        }

        _scanInProgress = true;
        _scanCancellation?.Cancel();
        _scanCancellation = new CancellationTokenSource();
        SetScanningState(true, reason);
        AddEventLog($"{reason}: запущено сканирование всей сети.");

        try
        {
            var progress = new Progress<ScanProgress>(UpdateProgress);
            var result = await _scanner.ScanLocalNetworksAsync(_manualIpAddresses, progress, _scanCancellation.Token);
            ApplyScanResult(result);
            RememberServerAddresses(result.Devices.Where(device => device.IsServer));
            lastUpdateLabel.Text = $"Последнее обновление: {result.CompletedAt:HH:mm:ss}";
            statusLabel.Text = $"Готово. Найдено устройств: {result.Devices.Count}";
            AddEventLog($"Сканирование всей сети завершено. Найдено устройств: {result.Devices.Count}, серверов: {result.Devices.Count(device => device.IsServer)}.");
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Сканирование остановлено.";
            AddEventLog("Сканирование всей сети остановлено.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Ошибка сканирования.";
            AddEventLog($"Ошибка сканирования всей сети: {ex.Message}");
            MessageBox.Show($"Ошибка сканирования сети: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetScanningState(false, "");
            _scanInProgress = false;
        }
    }

    private void ApplyScanResult(NetworkScanResult result)
    {
        var onlineAddresses = result.Devices.Select(device => device.IpAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var device in result.Devices)
        {
            UpsertDevice(device);
        }

        foreach (var address in result.ScannedAddresses)
        {
            if (onlineAddresses.Contains(address))
            {
                continue;
            }

            if (_devices.TryGetValue(address, out var existingDevice))
            {
                var wasOnline = existingDevice.IsOnline;
                existingDevice.IsOnline = false;
                existingDevice.NameResponded = false;
                existingDevice.CheckedAt = result.CompletedAt;
                existingDevice.Source = result.ManualAddresses.Contains(address) ? "Вручную" : existingDevice.Source;

                if (wasOnline)
                {
                    AddEventLog($"{address}: стал недоступен.");
                }
            }
            else if (result.ManualAddresses.Contains(address) && ManualIpStore.TryNormalize(address, out var normalized))
            {
                _devices[normalized] = new NetworkDevice
                {
                    IpAddress = normalized,
                    HostName = "Неизвестное устройство",
                    IsOnline = false,
                    CheckedAt = result.CompletedAt,
                    Source = "Вручную"
                };
                AddEventLog($"{normalized}: недоступен.");
            }
        }

        RenderDevices();
    }

    private async Task CheckAddressAsync(string ipAddress, string source, bool requireServerNameResponse = false)
    {
        if (!ManualIpStore.TryNormalize(ipAddress, out var normalized))
        {
            MessageBox.Show("Введите корректный IPv4-адрес.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ipTextBox.Focus();
            return;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        SetManualCheckState(false, $"Проверка {normalized}...");

        try
        {
            var requireNameResponse = requireServerNameResponse || IsKnownServerIp(normalized);
            var device = await _scanner.CheckIpAsync(IPAddress.Parse(normalized), source, requireNameResponse, cancellation.Token);
            UpsertDevice(device);
            if (device.IsServer)
            {
                RememberServerAddresses([device]);
            }

            RenderDevices();
            lastUpdateLabel.Text = $"Последняя проверка: {DateTime.Now:HH:mm:ss}";
            statusLabel.Text = $"{normalized}: {device.StatusText}";
            AddEventLog($"Ручная проверка {normalized}: {device.StatusText}.");
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = $"Проверка {normalized} остановлена по таймауту.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка проверки IP: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetManualCheckState(true, "");
        }
    }

    private void UpsertDevice(NetworkDevice device)
    {
        if (_devices.TryGetValue(device.IpAddress, out var existingDevice))
        {
            var wasOnline = existingDevice.IsOnline;
            var wasServer = existingDevice.IsServer;
            var preservedServerFlag = existingDevice.IsServer && !device.IsOnline;

            if (device.IsOnline)
            {
                existingDevice.HostName = device.HostName;
                existingDevice.MacAddress = device.MacAddress;
                existingDevice.OpenPorts = device.OpenPorts;
            }

            existingDevice.IsOnline = device.IsOnline;
            existingDevice.IsServer = device.IsServer || preservedServerFlag;
            existingDevice.NameResponded = device.NameResponded;
            existingDevice.CheckedAt = device.CheckedAt;
            existingDevice.Source = device.Source;

            if (wasOnline != existingDevice.IsOnline)
            {
                AddEventLog($"{existingDevice.IpAddress}: {existingDevice.StatusText}.");
            }

            if (!wasServer && existingDevice.IsServer)
            {
                AddEventLog($"Обнаружен сервер: {existingDevice.IpAddress} {existingDevice.HostName}.");
            }

            return;
        }

        _devices[device.IpAddress] = device;
        if (device.IsServer)
        {
            AddEventLog($"Обнаружен сервер: {device.IpAddress} {device.HostName}.");
        }
    }

    private void RenderDevices()
    {
        devicesGrid.SuspendLayout();
        devicesGrid.Rows.Clear();

        foreach (var device in _devices.Values
            .OrderByDescending(device => device.IsServer)
            .ThenByDescending(device => device.IsOnline)
            .ThenBy(device => NetworkScanner.ToSortableUInt32(IPAddress.Parse(device.IpAddress))))
        {
            var rowIndex = devicesGrid.Rows.Add(
                device.TypeText,
                device.HostName,
                device.IpAddress,
                device.MacAddress,
                device.OpenPorts,
                device.StatusText,
                device.CheckedAtText,
                device.Source);

            var row = devicesGrid.Rows[rowIndex];
            row.Tag = device;

            if (device.IsServer)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(232, 244, 255);
                row.DefaultCellStyle.Font = _serverRowFont;
            }

            row.Cells["Status"].Style.ForeColor = device.IsOnline ? Color.ForestGreen : Color.Firebrick;
        }

        devicesGrid.ResumeLayout();
    }

    private void UpdateProgress(ScanProgress progress)
    {
        statusLabel.Text = progress.Message;
        if (progress.Total <= 0)
        {
            scanProgressBar.Value = 0;
            return;
        }

        scanProgressBar.Maximum = progress.Total;
        scanProgressBar.Value = Math.Min(progress.Completed, progress.Total);
    }

    private void SetScanningState(bool isScanning, string status)
    {
        scanNetworkButton.Enabled = !isScanning;
        scanProgressBar.Visible = isScanning;
        scanProgressBar.Value = 0;
        if (!string.IsNullOrWhiteSpace(status))
        {
            statusLabel.Text = status;
        }
    }

    private void SetManualCheckState(bool enabled, string status)
    {
        addIpButton.Enabled = enabled;
        checkIpButton.Enabled = enabled;
        checkSelectedButton.Enabled = enabled;
        if (!string.IsNullOrWhiteSpace(status))
        {
            statusLabel.Text = status;
        }
    }

    private async void addIpButton_Click(object sender, EventArgs e)
    {
        if (!ManualIpStore.TryNormalize(ipTextBox.Text, out var normalized))
        {
            MessageBox.Show("Введите корректный IPv4-адрес.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ipTextBox.Focus();
            return;
        }

        if (!_manualIpAddresses.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            _manualIpAddresses.Add(normalized);
            _manualIpStore.Save(_manualIpAddresses);
            AddEventLog($"IP добавлен вручную: {normalized}.");
        }

        await CheckAddressAsync(normalized, "Вручную");
    }

    private async void checkIpButton_Click(object sender, EventArgs e)
    {
        await CheckAddressAsync(ipTextBox.Text, "Ручная проверка");
    }

    private async void checkSelectedButton_Click(object sender, EventArgs e)
    {
        if (devicesGrid.CurrentRow?.Tag is not NetworkDevice device)
        {
            MessageBox.Show("Выберите строку с IP-адресом.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await CheckAddressAsync(
            device.IpAddress,
            _manualIpAddresses.Contains(device.IpAddress, StringComparer.OrdinalIgnoreCase) ? "Вручную" : "Ручная проверка",
            device.IsServer);
    }

    private async void scanNetworkButton_Click(object sender, EventArgs e)
    {
        await RunNetworkScanAsync("Ручное сканирование всей сети");
    }

    private async void devicesGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || devicesGrid.Rows[e.RowIndex].Tag is not NetworkDevice device)
        {
            return;
        }

        await CheckAddressAsync(
            device.IpAddress,
            _manualIpAddresses.Contains(device.IpAddress, StringComparer.OrdinalIgnoreCase) ? "Вручную" : "Ручная проверка",
            device.IsServer);
    }

}
