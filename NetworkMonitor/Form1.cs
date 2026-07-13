using System.Net;

namespace NetworkMonitor;

public partial class Form1 : Form
{
    private readonly NetworkScanner _scanner = new();
    private readonly ManualIpStore _manualIpStore = new();
    private readonly Dictionary<string, NetworkDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _manualIpAddresses;
    private CancellationTokenSource? _scanCancellation;
    private bool _scanInProgress;
    private Font? _serverRowFont;

    public Form1()
    {
        _manualIpAddresses = _manualIpStore.Load();
        InitializeComponent();
        ConfigureGrid();

        autoScanTimer.Interval = 10 * 60 * 1000;
        autoScanTimer.Tick += async (_, _) => await RunNetworkScanAsync("Автоматическое сканирование");
        autoScanTimer.Start();

        Shown += async (_, _) => await RunNetworkScanAsync("Первичное сканирование");
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

        try
        {
            var progress = new Progress<ScanProgress>(UpdateProgress);
            var result = await _scanner.ScanLocalNetworksAsync(_manualIpAddresses, progress, _scanCancellation.Token);
            ApplyScanResult(result);
            lastUpdateLabel.Text = $"Последнее обновление: {result.CompletedAt:HH:mm:ss}";
            statusLabel.Text = $"Готово. Найдено устройств: {result.Devices.Count}";
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Сканирование остановлено.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Ошибка сканирования.";
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
                existingDevice.IsOnline = false;
                existingDevice.CheckedAt = result.CompletedAt;
                existingDevice.Source = result.ManualAddresses.Contains(address) ? "Вручную" : existingDevice.Source;
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
            }
        }

        RenderDevices();
    }

    private async Task CheckAddressAsync(string ipAddress, string source)
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
            var device = await _scanner.CheckIpAsync(IPAddress.Parse(normalized), source, cancellation.Token);
            UpsertDevice(device);
            RenderDevices();
            lastUpdateLabel.Text = $"Последняя проверка: {DateTime.Now:HH:mm:ss}";
            statusLabel.Text = $"{normalized}: {device.StatusText}";
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
            var preservedServerFlag = existingDevice.IsServer && !device.IsOnline;
            existingDevice.HostName = device.HostName;
            existingDevice.MacAddress = device.MacAddress;
            existingDevice.IsOnline = device.IsOnline;
            existingDevice.IsServer = device.IsServer || preservedServerFlag;
            existingDevice.CheckedAt = device.CheckedAt;
            existingDevice.Source = device.Source;
            existingDevice.OpenPorts = device.OpenPorts;
            return;
        }

        _devices[device.IpAddress] = device;
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

        await CheckAddressAsync(device.IpAddress, _manualIpAddresses.Contains(device.IpAddress, StringComparer.OrdinalIgnoreCase) ? "Вручную" : "Ручная проверка");
    }

    private async void scanNetworkButton_Click(object sender, EventArgs e)
    {
        await RunNetworkScanAsync("Ручное сканирование сети");
    }

    private async void devicesGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || devicesGrid.Rows[e.RowIndex].Tag is not NetworkDevice device)
        {
            return;
        }

        await CheckAddressAsync(device.IpAddress, _manualIpAddresses.Contains(device.IpAddress, StringComparer.OrdinalIgnoreCase) ? "Вручную" : "Ручная проверка");
    }

}
