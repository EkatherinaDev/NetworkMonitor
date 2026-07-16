using System.Net;
using System.Net.NetworkInformation;

namespace NetworkMonitor;

public partial class Form1 : Form
{
    private const int MaxEventLogItems = 500;
    private static readonly char[] IpAddressSeparators = [';', ',', '\r', '\n', '\t', ' '];
    private static readonly char[] ServiceAddressSeparators = [';', ',', '\r', '\n', '\t'];

    private readonly NetworkScanner _scanner = new();
    private readonly ServiceChecker _serviceChecker = new();
    private readonly ManualIpStore _manualIpStore = new();
    private readonly ManualIpStore _serverIpStore = new("server_ips.json");
    private readonly DefaultServerStore _defaultServerStore = new();
    private readonly ServiceEndpointStore _serviceEndpointStore = new();
    private readonly Dictionary<string, NetworkDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _manualIpAddresses;
    private readonly List<string> _serverIpAddresses;
    private readonly List<DefaultServerEndpoint> _defaultServers;
    private readonly List<ServiceEndpoint> _services;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _defaultServerCancellation;
    private CancellationTokenSource? _serviceCancellation;
    private bool _scanInProgress;
    private bool _defaultServerCheckInProgress;
    private bool _serviceCheckInProgress;
    private Font? _serverRowFont;
    private TabControl mainTabs = null!;
    private DataGridView defaultServersGrid = null!;
    private DataGridView servicesGrid = null!;
    private Button checkDefaultServersButton = null!;
    private TextBox serviceSearchTextBox = null!;
    private Button checkServiceSearchButton = null!;
    private Button checkServicesButton = null!;
    private ContextMenuStrip deviceContextMenu = null!;
    private ContextMenuStrip defaultServerContextMenu = null!;
    private ContextMenuStrip serviceContextMenu = null!;
    private ContextMenuStrip eventLogContextMenu = null!;
    private ToolStripMenuItem copyDeviceCellMenuItem = null!;
    private ToolStripMenuItem editDeviceMenuItem = null!;
    private ToolStripMenuItem deleteDeviceMenuItem = null!;
    private ToolStripMenuItem checkDeviceMenuItem = null!;
    private ToolStripMenuItem copyDefaultServerCellMenuItem = null!;
    private ToolStripMenuItem addDefaultServerMenuItem = null!;
    private ToolStripMenuItem editDefaultServerMenuItem = null!;
    private ToolStripMenuItem deleteDefaultServerMenuItem = null!;
    private ToolStripMenuItem checkDefaultServerMenuItem = null!;
    private ToolStripMenuItem copyServiceCellMenuItem = null!;
    private ToolStripMenuItem copyEventLogMenuItem = null!;
    private ToolStripMenuItem editServiceMenuItem = null!;
    private ToolStripMenuItem deleteServiceMenuItem = null!;
    private ToolStripMenuItem scanServiceMenuItem = null!;

    public Form1()
    {
        _manualIpAddresses = _manualIpStore.Load();
        _serverIpAddresses = _serverIpStore.Load();
        _defaultServers = _defaultServerStore.Load();
        _services = _serviceEndpointStore.Load();
        InitializeComponent();
        BuildTabbedLayout();
        ConfigureGrid();
        ConfigureDefaultServersGrid();
        ConfigureServicesGrid();
        ConfigureEventLogCopy();
        RenderDefaultServers();
        RenderServices();

        autoScanTimer.Interval = 10 * 60 * 1000;
        autoScanTimer.Tick += async (_, _) => await RunDefaultServerChecksAsync(_defaultServers, "Автоматическая проверка серверов");
        autoScanTimer.Start();

        Shown += async (_, _) =>
        {
            AddEventLog("Приложение запущено. Автоматически проверяются только серверы из вкладки Сервера.");
            await RunDefaultServerChecksAsync(_defaultServers, "Первичная проверка серверов");
        };
        FormClosing += (_, _) =>
        {
            _scanCancellation?.Cancel();
            _defaultServerCancellation?.Cancel();
            _serviceCancellation?.Cancel();
        };
    }

    private void ConfigureGrid()
    {
        _serverRowFont = new Font(devicesGrid.Font, FontStyle.Bold);
        deviceContextMenu = BuildDeviceContextMenu();
        devicesGrid.ContextMenuStrip = deviceContextMenu;
        devicesGrid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        devicesGrid.KeyDown += dataGridView_KeyDown;
        devicesGrid.MouseDown += devicesGrid_MouseDown;

        devicesGrid.Columns.Clear();
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Тип", Width = 105 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Имя / описание", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ip", HeaderText = "IP-адреса", Width = 190 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mac", HeaderText = "MAC-адреса", Width = 170 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ports", HeaderText = "Открытые порты", Width = 160 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Статус", Width = 115 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CheckedAt", HeaderText = "Проверено", Width = 105 });
        devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Источник", Width = 135 });
    }

    private ContextMenuStrip BuildDeviceContextMenu()
    {
        var contextMenu = new ContextMenuStrip();
        copyDeviceCellMenuItem = new ToolStripMenuItem("Копировать");
        editDeviceMenuItem = new ToolStripMenuItem("Редактировать");
        deleteDeviceMenuItem = new ToolStripMenuItem("Удалить");
        checkDeviceMenuItem = new ToolStripMenuItem("Проверить");

        copyDeviceCellMenuItem.Click += (_, _) => CopyCurrentGridCell(devicesGrid);
        editDeviceMenuItem.Click += editDeviceMenuItem_Click;
        deleteDeviceMenuItem.Click += deleteDeviceMenuItem_Click;
        checkDeviceMenuItem.Click += checkDeviceMenuItem_Click;

        contextMenu.Items.AddRange([
            copyDeviceCellMenuItem,
            new ToolStripSeparator(),
            editDeviceMenuItem,
            deleteDeviceMenuItem,
            new ToolStripSeparator(),
            checkDeviceMenuItem
        ]);

        contextMenu.Opening += (_, e) => e.Cancel = GetSelectedDeviceGroup() is null;
        return contextMenu;
    }

    private void ConfigureEventLogCopy()
    {
        eventLogContextMenu = new ContextMenuStrip();
        copyEventLogMenuItem = new ToolStripMenuItem("Копировать");
        copyEventLogMenuItem.Click += (_, _) => CopySelectedEventLogText();
        eventLogContextMenu.Items.Add(copyEventLogMenuItem);
        eventLogContextMenu.Opening += (_, e) => e.Cancel = eventLogListBox.SelectedItem is null;

        eventLogListBox.ContextMenuStrip = eventLogContextMenu;
        eventLogListBox.KeyDown += eventLogListBox_KeyDown;
        eventLogListBox.MouseDown += eventLogListBox_MouseDown;
    }

    private void BuildTabbedLayout()
    {
        rootLayout.SuspendLayout();

        rootLayout.Controls.Remove(actionPanel);
        rootLayout.Controls.Remove(devicesGrid);
        rootLayout.Controls.Remove(eventLogGroupBox);
        rootLayout.Controls.Remove(footerLayout);

        rootLayout.RowStyles.Clear();
        rootLayout.RowCount = 4;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        mainTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 0)
        };

        var defaultServersTab = new TabPage("Сервера");
        defaultServersTab.Controls.Add(BuildDefaultServersLayout());

        var networkTab = new TabPage("Сеть");
        var networkLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(0)
        };
        networkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        networkLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        networkLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        networkLayout.Controls.Add(actionPanel, 0, 0);
        networkLayout.Controls.Add(devicesGrid, 0, 1);
        networkTab.Controls.Add(networkLayout);

        var servicesTab = new TabPage("Сервисы");
        servicesTab.Controls.Add(BuildServicesLayout());

        mainTabs.TabPages.Add(defaultServersTab);
        mainTabs.TabPages.Add(networkTab);
        mainTabs.TabPages.Add(servicesTab);
        mainTabs.SelectedIndex = 0;

        rootLayout.Controls.Add(mainTabs, 0, 1);
        rootLayout.Controls.Add(eventLogGroupBox, 0, 2);
        rootLayout.Controls.Add(footerLayout, 0, 3);

        rootLayout.ResumeLayout(true);
    }

    private Control BuildDefaultServersLayout()
    {
        var serversLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(0)
        };
        serversLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        serversLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        serversLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var serversActionPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 8),
            WrapContents = true
        };

        checkDefaultServersButton = new Button
        {
            AutoSize = true,
            Text = "Проверить серверы",
            UseVisualStyleBackColor = true,
            Margin = new Padding(0, 2, 8, 2)
        };
        checkDefaultServersButton.Click += checkDefaultServersButton_Click;
        serversActionPanel.Controls.Add(checkDefaultServersButton);

        defaultServerContextMenu = BuildDefaultServerContextMenu();
        defaultServersGrid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            ContextMenuStrip = defaultServerContextMenu,
            Dock = DockStyle.Fill,
            EditMode = DataGridViewEditMode.EditProgrammatically,
            Margin = new Padding(0),
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersWidth = 48,
            RowTemplate = { Height = 30 },
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        defaultServersGrid.CellDoubleClick += defaultServersGrid_CellDoubleClick;
        defaultServersGrid.KeyDown += dataGridView_KeyDown;
        defaultServersGrid.MouseDown += defaultServersGrid_MouseDown;

        serversLayout.Controls.Add(serversActionPanel, 0, 0);
        serversLayout.Controls.Add(defaultServersGrid, 0, 1);
        return serversLayout;
    }

    private ContextMenuStrip BuildDefaultServerContextMenu()
    {
        var contextMenu = new ContextMenuStrip();
        copyDefaultServerCellMenuItem = new ToolStripMenuItem("Копировать");
        addDefaultServerMenuItem = new ToolStripMenuItem("Добавить");
        editDefaultServerMenuItem = new ToolStripMenuItem("Редактировать");
        deleteDefaultServerMenuItem = new ToolStripMenuItem("Удалить");
        checkDefaultServerMenuItem = new ToolStripMenuItem("Проверить");

        copyDefaultServerCellMenuItem.Click += (_, _) => CopyCurrentGridCell(defaultServersGrid);
        addDefaultServerMenuItem.Click += addDefaultServerMenuItem_Click;
        editDefaultServerMenuItem.Click += editDefaultServerMenuItem_Click;
        deleteDefaultServerMenuItem.Click += deleteDefaultServerMenuItem_Click;
        checkDefaultServerMenuItem.Click += checkDefaultServerMenuItem_Click;

        contextMenu.Items.AddRange([
            copyDefaultServerCellMenuItem,
            new ToolStripSeparator(),
            addDefaultServerMenuItem,
            editDefaultServerMenuItem,
            deleteDefaultServerMenuItem,
            new ToolStripSeparator(),
            checkDefaultServerMenuItem
        ]);

        contextMenu.Opening += (_, _) =>
        {
            var hasSelectedServer = GetSelectedDefaultServer() is not null;
            var canChangeServers = !_defaultServerCheckInProgress;
            copyDefaultServerCellMenuItem.Enabled = hasSelectedServer && defaultServersGrid.CurrentCell is not null;
            addDefaultServerMenuItem.Enabled = canChangeServers;
            editDefaultServerMenuItem.Enabled = hasSelectedServer && canChangeServers;
            deleteDefaultServerMenuItem.Enabled = hasSelectedServer && canChangeServers;
            checkDefaultServerMenuItem.Enabled = hasSelectedServer && canChangeServers;
        };

        return contextMenu;
    }

    private void ConfigureDefaultServersGrid()
    {
        defaultServersGrid.Columns.Clear();
        defaultServersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Сервер", Width = 150, ReadOnly = true });
        defaultServersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "IP-адреса", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 220 });
        defaultServersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mac", HeaderText = "MAC-адреса", Width = 160, ReadOnly = true });
        defaultServersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ports", HeaderText = "Открытые порты", Width = 155, ReadOnly = true });
        defaultServersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Статус", Width = 115, ReadOnly = true });
        defaultServersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ping", HeaderText = "Ping", Width = 110, ReadOnly = true });
        defaultServersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rdp", HeaderText = "3389 / сертификат", Width = 150, ReadOnly = true });
        defaultServersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DetectedNames", HeaderText = "Найденные имена", Width = 170, ReadOnly = true });
        defaultServersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CheckedAt", HeaderText = "Проверено", Width = 105, ReadOnly = true });
    }

    private void RenderDefaultServers()
    {
        defaultServersGrid.SuspendLayout();
        defaultServersGrid.Rows.Clear();

        foreach (var server in _defaultServers)
        {
            var rowIndex = defaultServersGrid.Rows.Add(
                server.Name,
                server.Address,
                server.MacAddresses,
                server.OpenPorts,
                server.CheckedAt is null ? "Не проверено" : server.StatusText,
                server.PingStatus,
                server.RdpStatus,
                server.DetectedNames,
                server.CheckedAtText);

            var row = defaultServersGrid.Rows[rowIndex];
            row.Tag = server;

            if (server.CheckedAt is not null)
            {
                row.Cells["Status"].Style.ForeColor = server.IsOnline ? Color.ForestGreen : Color.Firebrick;
            }
        }

        defaultServersGrid.ResumeLayout();
    }

    private void SaveDefaultServers()
    {
        _defaultServerStore.Save(_defaultServers);
    }

    private DefaultServerEndpoint? GetSelectedDefaultServer()
    {
        return defaultServersGrid.CurrentRow?.Tag as DefaultServerEndpoint;
    }

    private void SelectDefaultServerRow(DefaultServerEndpoint server)
    {
        foreach (DataGridViewRow row in defaultServersGrid.Rows)
        {
            if (!ReferenceEquals(row.Tag, server))
            {
                continue;
            }

            defaultServersGrid.ClearSelection();
            row.Selected = true;
            defaultServersGrid.CurrentCell = row.Cells[0];
            return;
        }
    }

    private bool HasDefaultServerName(string name, DefaultServerEndpoint? excludedServer = null)
    {
        return _defaultServers.Any(server =>
            !ReferenceEquals(server, excludedServer)
            && server.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RunDefaultServerChecksAsync(IEnumerable<DefaultServerEndpoint> serversToCheck, string reason)
    {
        if (_defaultServerCheckInProgress || _scanInProgress)
        {
            statusLabel.Text = "Другая сетевая проверка уже выполняется.";
            return;
        }

        var selectedServers = serversToCheck.Distinct().ToList();
        if (selectedServers.Count == 0)
        {
            statusLabel.Text = "Нет серверов для проверки.";
            AddEventLog("Проверка серверов пропущена: список пуст.");
            return;
        }

        var serverIpMap = new Dictionary<DefaultServerEndpoint, List<string>>();
        var invalidServers = new List<(DefaultServerEndpoint Server, string InvalidValue)>();

        foreach (var server in selectedServers)
        {
            if (!TryParseIpAddressList(server.Address, out var ipAddresses, out var invalidValue))
            {
                invalidServers.Add((server, invalidValue));
                continue;
            }

            if (ipAddresses.Count == 0)
            {
                invalidServers.Add((server, "пустой список IP"));
                continue;
            }

            serverIpMap[server] = ipAddresses;
        }

        _defaultServerCheckInProgress = true;
        _defaultServerCancellation?.Cancel();
        _defaultServerCancellation = new CancellationTokenSource();
        SetDefaultServerCheckState(false, reason);
        AddEventLog($"{reason}: серверов {selectedServers.Count}.");

        try
        {
            foreach (var (server, invalidValue) in invalidServers)
            {
                MarkDefaultServerInvalid(server, $"Некорректный IP: {invalidValue}");
                AddEventLog($"{server.Name}: некорректный IP: {invalidValue}.");
            }

            var allIpAddresses = serverIpMap.Values
                .SelectMany(ipAddresses => ipAddresses)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => NetworkScanner.ToSortableUInt32(IPAddress.Parse(value)))
                .ToList();

            if (allIpAddresses.Count > 0)
            {
                var progress = new Progress<ScanProgress>(UpdateProgress);
                var result = await _scanner.CheckAddressesAsync(
                    allIpAddresses,
                    reason,
                    progress,
                    _defaultServerCancellation.Token,
                    forceServer: true);

                var devicesByIp = result.Devices.ToDictionary(device => device.IpAddress, StringComparer.OrdinalIgnoreCase);
                var pingResults = await PingIpAddressesAsync(allIpAddresses, _defaultServerCancellation.Token);

                foreach (var (server, ipAddresses) in serverIpMap)
                {
                    ApplyDefaultServerCheckResult(server, ipAddresses, devicesByIp, pingResults);
                    AddDefaultServerCheckDetailsToEventLog(server);
                }
            }

            RenderDefaultServers();
            lastUpdateLabel.Text = $"Последняя проверка серверов: {DateTime.Now:HH:mm:ss}";

            var onlineCount = selectedServers.Count(server => server.CheckedAt is not null && server.IsOnline);
            var offlineCount = selectedServers.Count(server => server.CheckedAt is not null && !server.IsOnline);
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
            SetDefaultServerCheckState(true, "");
            _defaultServerCheckInProgress = false;
        }
    }

    private void SetDefaultServerCheckState(bool enabled, string status)
    {
        checkDefaultServersButton.Enabled = enabled;
        addDefaultServerMenuItem.Enabled = enabled;
        editDefaultServerMenuItem.Enabled = enabled;
        deleteDefaultServerMenuItem.Enabled = enabled;
        checkDefaultServerMenuItem.Enabled = enabled;
        scanProgressBar.Visible = !enabled;
        scanProgressBar.Value = 0;

        if (!string.IsNullOrWhiteSpace(status))
        {
            statusLabel.Text = status;
        }
    }

    private static async Task<IReadOnlyDictionary<string, bool>> PingIpAddressesAsync(
        IReadOnlyList<string> ipAddresses,
        CancellationToken cancellationToken)
    {
        var pingTasks = ipAddresses
            .Select(async ipAddress => new KeyValuePair<string, bool>(
                ipAddress,
                await PingIpAddressAsync(ipAddress, cancellationToken)))
            .ToArray();

        var results = await Task.WhenAll(pingTasks);
        return results.ToDictionary(result => result.Key, result => result.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<bool> PingIpAddressAsync(string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(IPAddress.Parse(ipAddress), 1000).WaitAsync(cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void ApplyDefaultServerCheckResult(
        DefaultServerEndpoint server,
        IReadOnlyList<string> ipAddresses,
        IReadOnlyDictionary<string, NetworkDevice> devicesByIp,
        IReadOnlyDictionary<string, bool> pingResults)
    {
        var devices = ipAddresses
            .Select(ipAddress => devicesByIp.TryGetValue(ipAddress, out var device)
                ? device
                : CreateUnavailableDefaultServerDevice(ipAddress))
            .OrderBy(device => NetworkScanner.ToSortableUInt32(IPAddress.Parse(device.IpAddress)))
            .ToList();

        var serverPingResults = ipAddresses.ToDictionary(
            ipAddress => ipAddress,
            ipAddress => pingResults.TryGetValue(ipAddress, out var isOnline) && isOnline,
            StringComparer.OrdinalIgnoreCase);

        var pingCount = serverPingResults.Count(pair => pair.Value);
        var certificateCount = devices.Count(device => device.RdpCertificateResponded);
        var rdpOpenCount = devices.Count(device => device.IsOnline);

        server.LastDevices = devices;
        server.LastPingResults = serverPingResults;
        server.IsOnline = devices.Any(device =>
            device.RdpCertificateResponded
            && serverPingResults.TryGetValue(device.IpAddress, out var pingOk)
            && pingOk);
        server.CheckedAt = DateTime.Now;
        server.PingStatus = $"{pingCount}/{ipAddresses.Count} OK";
        server.RdpStatus = certificateCount > 0
            ? $"{certificateCount}/{ipAddresses.Count} сертификат"
            : rdpOpenCount > 0
                ? $"{rdpOpenCount}/{ipAddresses.Count} 3389 открыт"
                : "3389 закрыт";
        server.DetectedNames = FormatDefaultServerNames(devices);
        server.MacAddresses = FormatDefaultServerMacs(devices);
        server.OpenPorts = FormatDefaultServerPorts(devices);
        server.Details = string.Join(Environment.NewLine, devices.Select(device => FormatDefaultServerIpLog(server, device)));
    }

    private static NetworkDevice CreateUnavailableDefaultServerDevice(string ipAddress)
    {
        return new NetworkDevice
        {
            IpAddress = ipAddress,
            HostName = NetworkDevice.UnknownHostName,
            IsServer = true,
            IsOnline = false,
            CheckedAt = DateTime.Now,
            Source = "Проверка серверов"
        };
    }

    private static void MarkDefaultServerInvalid(DefaultServerEndpoint server, string details)
    {
        server.IsOnline = false;
        server.PingStatus = "Не проверено";
        server.RdpStatus = "Не проверено";
        server.DetectedNames = "";
        server.MacAddresses = "";
        server.OpenPorts = "";
        server.CheckedAt = DateTime.Now;
        server.Details = details;
        server.LastDevices = [];
        server.LastPingResults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    private void AddDefaultServerCheckDetailsToEventLog(DefaultServerEndpoint server)
    {
        AddEventLog($"Проверка {server.Name}:");

        if (server.LastDevices.Count == 0)
        {
            AddEventLog($"{server.Name}: {server.Details}");
            return;
        }

        foreach (var device in server.LastDevices)
        {
            AddEventLog(FormatDefaultServerIpLog(server, device));
        }
    }

    private static string FormatDefaultServerIpLog(DefaultServerEndpoint server, NetworkDevice device)
    {
        var pingOk = server.LastPingResults.TryGetValue(device.IpAddress, out var isPingOnline) && isPingOnline;
        var status = device.RdpCertificateResponded && pingOk ? "В сети" : "Недоступен";
        var pingText = pingOk ? "ping OK" : "ping нет";
        var certificateText = device.RdpCertificateResponded
            ? string.IsNullOrWhiteSpace(device.RdpCertificateName)
                ? "сертификат получен"
                : $"сертификат: {device.RdpCertificateName}"
            : "сертификат не получен";
        var ports = string.IsNullOrWhiteSpace(device.OpenPorts)
            ? "открытых портов нет"
            : $"порты: {device.OpenPorts}";

        return $"{device.IpAddress}: {status}, {pingText}, {certificateText}, {ports}";
    }

    private static string FormatDefaultServerNames(IEnumerable<NetworkDevice> devices)
    {
        var names = devices
            .SelectMany(device => new[] { device.RdpCertificateName, device.HostName })
            .Where(NetworkDevice.IsKnownHostName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join("; ", names);
    }

    private static string FormatDefaultServerMacs(IEnumerable<NetworkDevice> devices)
    {
        return string.Join("; ", devices
            .Select(device => device.MacAddress)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string FormatDefaultServerPorts(IEnumerable<NetworkDevice> devices)
    {
        var ports = devices
            .SelectMany(device => device.OpenPorts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(value => int.TryParse(value, out var port) ? port : 0)
            .Where(port => port > 0)
            .Distinct()
            .OrderBy(port => port)
            .ToList();

        return string.Join(", ", ports);
    }

    private bool EditDefaultServer(DefaultServerEndpoint server, string title)
    {
        using var dialog = new Form
        {
            AutoScaleMode = AutoScaleMode.Font,
            ClientSize = new Size(540, 170),
            Font = Font,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            Text = title
        };

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var nameLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 8, 6),
            Text = "Название:"
        };
        var nameBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3),
            Text = server.Name
        };

        var addressLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 8, 6),
            Text = "IP-адреса:"
        };
        var addressBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3),
            Text = server.Address
        };

        var buttonsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 12, 0, 0)
        };

        var saveButton = new Button
        {
            AutoSize = true,
            Text = "Сохранить",
            UseVisualStyleBackColor = true
        };
        var cancelButton = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Text = "Отмена",
            UseVisualStyleBackColor = true
        };

        saveButton.Click += (_, _) =>
        {
            var updatedName = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(updatedName))
            {
                MessageBox.Show("Введите название сервера.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                nameBox.Focus();
                return;
            }

            if (HasDefaultServerName(updatedName, server))
            {
                MessageBox.Show("Сервер с таким названием уже есть в списке.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                nameBox.Focus();
                return;
            }

            if (!TryParseIpAddressList(addressBox.Text, out var updatedIpAddresses, out var invalidValue))
            {
                MessageBox.Show($"Некорректный IPv4-адрес: {invalidValue}", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                addressBox.Focus();
                return;
            }

            if (updatedIpAddresses.Count == 0)
            {
                MessageBox.Show("Введите хотя бы один IPv4-адрес.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                addressBox.Focus();
                return;
            }

            server.Name = updatedName;
            server.Address = string.Join("; ", updatedIpAddresses);
            MarkDefaultServerInvalid(server, "Не проверено");
            server.CheckedAt = null;
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };

        buttonsPanel.Controls.Add(saveButton);
        buttonsPanel.Controls.Add(cancelButton);

        layout.Controls.Add(nameLabel, 0, 0);
        layout.Controls.Add(nameBox, 1, 0);
        layout.Controls.Add(addressLabel, 0, 1);
        layout.Controls.Add(addressBox, 1, 1);
        layout.Controls.Add(buttonsPanel, 0, 3);
        layout.SetColumnSpan(buttonsPanel, 2);

        dialog.AcceptButton = saveButton;
        dialog.CancelButton = cancelButton;
        dialog.Controls.Add(layout);

        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    private Control BuildServicesLayout()
    {
        var servicesLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(0)
        };
        servicesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        servicesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        servicesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        servicesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var serviceSearchPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 4),
            WrapContents = true
        };

        var serviceAddressLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 7, 8, 0),
            Text = "DNS-имя или IP:"
        };

        serviceSearchTextBox = new TextBox
        {
            Margin = new Padding(0, 3, 8, 3),
            PlaceholderText = "server.domain.local или 192.168.1.10",
            Size = new Size(360, 27)
        };
        serviceSearchTextBox.KeyDown += serviceSearchTextBox_KeyDown;

        checkServiceSearchButton = new Button
        {
            AutoSize = true,
            Text = "Проверить",
            UseVisualStyleBackColor = true,
            Margin = new Padding(0, 2, 8, 2)
        };
        checkServiceSearchButton.Click += checkServiceSearchButton_Click;

        serviceSearchPanel.Controls.Add(serviceAddressLabel);
        serviceSearchPanel.Controls.Add(serviceSearchTextBox);
        serviceSearchPanel.Controls.Add(checkServiceSearchButton);

        var servicesActionPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            WrapContents = true
        };

        checkServicesButton = new Button
        {
            AutoSize = true,
            Text = "Проверить сервисы",
            UseVisualStyleBackColor = true,
            Margin = new Padding(0, 2, 8, 2)
        };
        checkServicesButton.Click += checkServicesButton_Click;

        servicesActionPanel.Controls.Add(checkServicesButton);

        serviceContextMenu = BuildServiceContextMenu();
        servicesGrid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            ContextMenuStrip = serviceContextMenu,
            Dock = DockStyle.Fill,
            EditMode = DataGridViewEditMode.EditProgrammatically,
            Margin = new Padding(0),
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersWidth = 48,
            RowTemplate = { Height = 30 },
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        servicesGrid.CellDoubleClick += servicesGrid_CellDoubleClick;
        servicesGrid.KeyDown += dataGridView_KeyDown;
        servicesGrid.MouseDown += servicesGrid_MouseDown;

        servicesLayout.Controls.Add(serviceSearchPanel, 0, 0);
        servicesLayout.Controls.Add(servicesActionPanel, 0, 1);
        servicesLayout.Controls.Add(servicesGrid, 0, 2);
        return servicesLayout;
    }

    private ContextMenuStrip BuildServiceContextMenu()
    {
        var contextMenu = new ContextMenuStrip();
        copyServiceCellMenuItem = new ToolStripMenuItem("Копировать");
        editServiceMenuItem = new ToolStripMenuItem("Редактировать");
        deleteServiceMenuItem = new ToolStripMenuItem("Удалить");
        scanServiceMenuItem = new ToolStripMenuItem("Сканировать");

        copyServiceCellMenuItem.Click += (_, _) => CopyCurrentGridCell(servicesGrid);
        editServiceMenuItem.Click += editServiceMenuItem_Click;
        deleteServiceMenuItem.Click += deleteServiceMenuItem_Click;
        scanServiceMenuItem.Click += scanServiceMenuItem_Click;

        contextMenu.Items.AddRange([
            copyServiceCellMenuItem,
            new ToolStripSeparator(),
            editServiceMenuItem,
            deleteServiceMenuItem,
            new ToolStripSeparator(),
            scanServiceMenuItem
        ]);

        contextMenu.Opening += (_, e) => e.Cancel = GetSelectedService() is null;
        return contextMenu;
    }

    private void ConfigureServicesGrid()
    {
        servicesGrid.Columns.Clear();
        servicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Сервис", Width = 135, ReadOnly = true });
        servicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "DNS-имя или IP", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 280 });
        servicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ip", HeaderText = "IPv4", Width = 135, ReadOnly = true });
        servicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Статус", Width = 115, ReadOnly = true });
        servicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CheckedAt", HeaderText = "Проверено", Width = 105, ReadOnly = true });
    }

    private void RenderServices()
    {
        servicesGrid.SuspendLayout();
        servicesGrid.Rows.Clear();

        foreach (var service in _services)
        {
            var rowIndex = servicesGrid.Rows.Add(
                service.Name,
                service.Address,
                service.ResolvedIp,
                service.CheckedAt is null ? "Не проверено" : service.StatusText,
                service.CheckedAtText);

            var row = servicesGrid.Rows[rowIndex];
            row.Tag = service;

            if (service.CheckedAt is not null)
            {
                row.Cells["Status"].Style.ForeColor = service.IsOnline ? Color.ForestGreen : Color.Firebrick;
            }
        }

        servicesGrid.ResumeLayout();
    }

    private void SaveServices()
    {
        _serviceEndpointStore.Save(_services);
    }

    private async Task RunServiceChecksAsync(IEnumerable<ServiceEndpoint> servicesToCheck)
    {
        if (_serviceCheckInProgress)
        {
            return;
        }

        var selectedServices = servicesToCheck.ToList();
        if (selectedServices.Count == 0)
        {
            return;
        }

        _serviceCheckInProgress = true;
        _serviceCancellation?.Cancel();
        _serviceCancellation = new CancellationTokenSource();
        SetServiceCheckState(false);
        AddEventLog($"Проверка сервисов: {selectedServices.Count}.");

        try
        {
            var progress = new Progress<ScanProgress>(UpdateProgress);
            var results = await _serviceChecker.CheckAsync(selectedServices, progress, _serviceCancellation.Token);

            foreach (var result in results)
            {
                var service = _services.First(item => item.Name.Equals(result.Name, StringComparison.OrdinalIgnoreCase));
                service.Address = result.Address;
                service.ResolvedIp = result.ResolvedIp;
                service.IsOnline = result.IsOnline;
                service.CheckedAt = result.CheckedAt;
                service.Details = result.Details;
            }

            RenderServices();

            var onlineCount = results.Count(service => service.IsOnline);
            var offlineCount = results.Count - onlineCount;
            statusLabel.Text = $"Проверка сервисов завершена. В сети: {onlineCount}, недоступно: {offlineCount}";
            AddEventLog($"Проверка сервисов завершена: в сети {onlineCount}, недоступно {offlineCount}.");
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Проверка сервисов остановлена.";
            AddEventLog("Проверка сервисов остановлена.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Ошибка проверки сервисов.";
            AddEventLog($"Ошибка проверки сервисов: {ex.Message}");
            MessageBox.Show($"Ошибка проверки сервисов: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetServiceCheckState(true);
            _serviceCheckInProgress = false;
        }
    }

    private void SetServiceCheckState(bool enabled)
    {
        checkServicesButton.Enabled = enabled;
        checkServiceSearchButton.Enabled = enabled;
        serviceSearchTextBox.Enabled = enabled;
        copyServiceCellMenuItem.Enabled = enabled;
        editServiceMenuItem.Enabled = enabled;
        deleteServiceMenuItem.Enabled = enabled;
        scanServiceMenuItem.Enabled = enabled;
    }

    private ServiceEndpoint? GetSelectedService()
    {
        return servicesGrid.CurrentRow?.Tag as ServiceEndpoint;
    }

    private NetworkDeviceGroup? GetSelectedDeviceGroup()
    {
        return devicesGrid.CurrentRow?.Tag as NetworkDeviceGroup;
    }

    private void SelectServiceRow(ServiceEndpoint service)
    {
        foreach (DataGridViewRow row in servicesGrid.Rows)
        {
            if (!ReferenceEquals(row.Tag, service))
            {
                continue;
            }

            servicesGrid.ClearSelection();
            row.Selected = true;
            servicesGrid.CurrentCell = row.Cells[0];
            return;
        }
    }

    private bool HasServiceName(string name, ServiceEndpoint? excludedService = null)
    {
        return _services.Any(service =>
            !ReferenceEquals(service, excludedService)
            && service.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private ServiceEndpoint? FindServiceByAddressOrIp(string value)
    {
        return _services.FirstOrDefault(service =>
            service.Address.Equals(value, StringComparison.OrdinalIgnoreCase)
            || ServiceAddressContains(service.Address, value)
            || service.ResolvedIp.Equals(value, StringComparison.OrdinalIgnoreCase)
            || service.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ServiceAddressContains(string address, string value)
    {
        return address
            .Split(ServiceAddressSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => candidate.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private async Task CheckServiceSearchAsync()
    {
        var address = serviceSearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            MessageBox.Show("Введите DNS-имя или IP.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            serviceSearchTextBox.Focus();
            return;
        }

        var service = FindServiceByAddressOrIp(address);
        if (service is null)
        {
            service = new ServiceEndpoint
            {
                Name = address,
                Address = address
            };

            _services.Add(service);
            SaveServices();
            RenderServices();
            AddEventLog($"Адрес добавлен для проверки сервисов: {service.Address}.");
        }
        else if (string.IsNullOrWhiteSpace(service.Address))
        {
            service.Address = address;
            SaveServices();
        }

        SelectServiceRow(service);
        await RunServiceChecksAsync([service]);
        SelectServiceRow(service);

        serviceSearchTextBox.SelectAll();
        serviceSearchTextBox.Focus();
    }

    private bool EditService(ServiceEndpoint service)
    {
        using var dialog = new Form
        {
            AutoScaleMode = AutoScaleMode.Font,
            ClientSize = new Size(520, 170),
            Font = Font,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            Text = "Редактировать сервис"
        };

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var nameLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 8, 6),
            Text = "Название:"
        };
        var nameBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3),
            Text = service.Name
        };

        var addressLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 8, 6),
            Text = "DNS-имя или IP:"
        };
        var addressBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3),
            Text = service.Address
        };

        var buttonsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 12, 0, 0)
        };

        var saveButton = new Button
        {
            AutoSize = true,
            Text = "Сохранить",
            UseVisualStyleBackColor = true
        };
        var cancelButton = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Text = "Отмена",
            UseVisualStyleBackColor = true
        };

        saveButton.Click += (_, _) =>
        {
            var updatedName = nameBox.Text.Trim();
            var updatedAddress = addressBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(updatedName))
            {
                MessageBox.Show("Введите название сервиса.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                nameBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(updatedAddress))
            {
                MessageBox.Show("Введите DNS-имя или IP сервиса.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                addressBox.Focus();
                return;
            }

            if (HasServiceName(updatedName, service))
            {
                MessageBox.Show("Сервис с таким названием уже есть в списке.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                nameBox.Focus();
                return;
            }

            service.Name = updatedName;
            service.Address = updatedAddress;
            service.ResolvedIp = "";
            service.CheckedAt = null;
            service.IsOnline = false;
            service.Details = "Не проверено";
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };

        buttonsPanel.Controls.Add(saveButton);
        buttonsPanel.Controls.Add(cancelButton);

        layout.Controls.Add(nameLabel, 0, 0);
        layout.Controls.Add(nameBox, 1, 0);
        layout.Controls.Add(addressLabel, 0, 1);
        layout.Controls.Add(addressBox, 1, 1);
        layout.Controls.Add(buttonsPanel, 0, 3);
        layout.SetColumnSpan(buttonsPanel, 2);

        dialog.AcceptButton = saveButton;
        dialog.CancelButton = cancelButton;
        dialog.Controls.Add(layout);

        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    private bool EditDeviceGroup(NetworkDeviceGroup group)
    {
        using var dialog = new Form
        {
            AutoScaleMode = AutoScaleMode.Font,
            ClientSize = new Size(560, 230),
            Font = Font,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            Text = "Редактировать строку"
        };

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var nameLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 8, 6),
            Text = "Имя / описание:"
        };
        var nameBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3),
            PlaceholderText = NetworkDevice.UnknownHostName,
            Text = group.HostName.Equals(NetworkDevice.UnknownHostName, StringComparison.OrdinalIgnoreCase) ? "" : group.HostName
        };

        var ipLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 8, 6),
            Text = "IP-адреса:"
        };
        var ipBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, group.Devices.Select(device => device.IpAddress))
        };

        var buttonsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 12, 0, 0)
        };

        var saveButton = new Button
        {
            AutoSize = true,
            Text = "Сохранить",
            UseVisualStyleBackColor = true
        };
        var cancelButton = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Text = "Отмена",
            UseVisualStyleBackColor = true
        };

        saveButton.Click += (_, _) =>
        {
            var updatedName = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(updatedName))
            {
                updatedName = NetworkDevice.UnknownHostName;
            }

            if (!TryParseIpAddressList(ipBox.Text, out var updatedIpAddresses, out var invalidValue))
            {
                MessageBox.Show($"Некорректный IPv4-адрес: {invalidValue}", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ipBox.Focus();
                return;
            }

            if (updatedIpAddresses.Count == 0)
            {
                MessageBox.Show("Введите хотя бы один IPv4-адрес.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ipBox.Focus();
                return;
            }

            var oldIpAddresses = group.Devices.Select(device => device.IpAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingIp = updatedIpAddresses.FirstOrDefault(ipAddress =>
                _devices.ContainsKey(ipAddress) && !oldIpAddresses.Contains(ipAddress));
            if (existingIp is not null)
            {
                MessageBox.Show($"IP-адрес {existingIp} уже есть в списке.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ipBox.Focus();
                return;
            }

            ApplyDeviceGroupEdit(group, updatedName, updatedIpAddresses);
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };

        buttonsPanel.Controls.Add(saveButton);
        buttonsPanel.Controls.Add(cancelButton);

        layout.Controls.Add(nameLabel, 0, 0);
        layout.Controls.Add(nameBox, 1, 0);
        layout.Controls.Add(ipLabel, 0, 1);
        layout.Controls.Add(ipBox, 1, 1);
        layout.Controls.Add(buttonsPanel, 0, 2);
        layout.SetColumnSpan(buttonsPanel, 2);

        dialog.AcceptButton = saveButton;
        dialog.CancelButton = cancelButton;
        dialog.Controls.Add(layout);

        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    private void ApplyDeviceGroupEdit(NetworkDeviceGroup group, string updatedName, IReadOnlyList<string> updatedIpAddresses)
    {
        var oldIpAddresses = group.Devices
            .Select(device => device.IpAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var updatedIpSet = updatedIpAddresses.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var oldIpAddress in oldIpAddresses.Where(ipAddress => !updatedIpSet.Contains(ipAddress)))
        {
            _devices.Remove(oldIpAddress);
        }

        foreach (var ipAddress in updatedIpAddresses)
        {
            if (!_devices.TryGetValue(ipAddress, out var device))
            {
                device = new NetworkDevice
                {
                    IpAddress = ipAddress,
                    CheckedAt = DateTime.Now
                };
                _devices[ipAddress] = device;
            }

            device.HostName = updatedName;
            device.Source = "Вручную";
            device.IsServer = device.IsServer || group.IsServer;
        }

        ReplaceAddresses(_manualIpAddresses, oldIpAddresses, updatedIpAddresses);
        _manualIpStore.Save(_manualIpAddresses);
        _manualIpAddresses.Clear();
        _manualIpAddresses.AddRange(_manualIpStore.Load());

        ReplaceAddresses(_serverIpAddresses, oldIpAddresses, group.IsServer ? updatedIpAddresses : []);
        _serverIpStore.Save(_serverIpAddresses);
        _serverIpAddresses.Clear();
        _serverIpAddresses.AddRange(_serverIpStore.Load());

        AddEventLog($"Строка изменена: {group.HostName} -> {updatedName} ({string.Join("; ", updatedIpAddresses)}).");
    }

    private void DeleteDeviceGroup(NetworkDeviceGroup group)
    {
        var ipAddresses = group.Devices
            .Select(device => device.IpAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => NetworkScanner.ToSortableUInt32(IPAddress.Parse(value)))
            .ToList();

        var result = MessageBox.Show(
            $"Удалить строку \"{group.HostName}\"?\n\nIP: {string.Join("; ", ipAddresses)}",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        foreach (var ipAddress in ipAddresses)
        {
            _devices.Remove(ipAddress);
        }

        var changedManual = RemoveAddresses(_manualIpAddresses, ipAddresses);
        if (changedManual)
        {
            _manualIpStore.Save(_manualIpAddresses);
        }

        var changedServers = RemoveAddresses(_serverIpAddresses, ipAddresses);
        if (changedServers)
        {
            _serverIpStore.Save(_serverIpAddresses);
        }

        RenderDevices();
        AddEventLog($"Строка удалена: {group.HostName} ({string.Join("; ", ipAddresses)}).");
        statusLabel.Text = $"Удалено IP из списка: {ipAddresses.Count}";
    }

    private static bool TryParseIpAddressList(string value, out List<string> ipAddresses, out string invalidValue)
    {
        ipAddresses = [];
        invalidValue = "";

        foreach (var item in value.Split(IpAddressSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ManualIpStore.TryNormalize(item, out var normalized))
            {
                invalidValue = item;
                return false;
            }

            if (!ipAddresses.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                ipAddresses.Add(normalized);
            }
        }

        return true;
    }

    private static void ReplaceAddresses(List<string> target, IEnumerable<string> oldIpAddresses, IEnumerable<string> newIpAddresses)
    {
        RemoveAddresses(target, oldIpAddresses);
        foreach (var ipAddress in newIpAddresses)
        {
            if (!target.Contains(ipAddress, StringComparer.OrdinalIgnoreCase))
            {
                target.Add(ipAddress);
            }
        }
    }

    private static bool RemoveAddresses(List<string> target, IEnumerable<string> ipAddresses)
    {
        var ipAddressSet = ipAddresses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return target.RemoveAll(ipAddress => ipAddressSet.Contains(ipAddress)) > 0;
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

    private void AddDeviceCheckDetailsToEventLog(string title, IEnumerable<NetworkDevice> devices)
    {
        AddEventLog($"Проверка {title}:");

        foreach (var device in devices.OrderBy(device => NetworkScanner.ToSortableUInt32(IPAddress.Parse(device.IpAddress))))
        {
            AddEventLog(FormatDeviceCheckLog(device));
        }
    }

    private static string FormatDeviceCheckLog(NetworkDevice device)
    {
        var ports = string.IsNullOrWhiteSpace(device.OpenPorts)
            ? "открытых портов нет"
            : $"порты: {device.OpenPorts}";

        if (!device.IsOnline)
        {
            return $"{device.IpAddress}: Недоступен, {ports}";
        }

        return $"{device.IpAddress}: В сети, {ports}";
    }

    private bool IsKnownServerIp(string ipAddress)
    {
        return _serverIpAddresses.Contains(ipAddress, StringComparer.OrdinalIgnoreCase)
            || (_devices.TryGetValue(ipAddress, out var device) && device.IsServer);
    }

    private async Task RunServerScanAsync(string reason)
    {
        if (_scanInProgress || _defaultServerCheckInProgress)
        {
            statusLabel.Text = "Другая сетевая проверка уже выполняется.";
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
        if (_scanInProgress || _defaultServerCheckInProgress)
        {
            statusLabel.Text = "Другая сетевая проверка уже выполняется.";
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
            statusLabel.Text = $"Готово. Найдено IP с открытыми портами: {result.Devices.Count}";
            AddEventLog($"Сканирование всей сети завершено. Найдено IP с открытыми портами: {result.Devices.Count}, серверов: {result.Devices.Count(device => device.IsServer)}.");
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
                existingDevice.OpenPorts = "";
                existingDevice.CheckedAt = result.CompletedAt;
                existingDevice.Source = result.ManualAddresses.Contains(address) ? "Вручную (нет открытых портов)" : "Автосканирование (нет открытых портов)";

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
                    HostName = NetworkDevice.UnknownHostName,
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

    private async Task CheckDeviceGroupAsync(NetworkDeviceGroup group)
    {
        var ipAddresses = group.Devices
            .Select(device => device.IpAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => NetworkScanner.ToSortableUInt32(IPAddress.Parse(value)))
            .ToList();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(8, ipAddresses.Count * 3)));
        SetManualCheckState(false, $"Проверка {ipAddresses.Count} IP...");

        try
        {
            var progress = new Progress<ScanProgress>(UpdateProgress);
            var result = await _scanner.CheckAddressesAsync(ipAddresses, "Ручная проверка", progress, cancellation.Token, forceServer: group.IsServer);

            foreach (var device in result.Devices)
            {
                device.IsServer = device.IsServer || group.IsServer;
                UpsertDevice(device);
            }

            RememberServerAddresses(result.Devices.Where(device => device.IsServer));
            RenderDevices();
            lastUpdateLabel.Text = $"Последняя проверка: {DateTime.Now:HH:mm:ss}";

            var onlineCount = result.Devices.Count(device => device.IsOnline);
            var offlineCount = result.Devices.Count - onlineCount;
            statusLabel.Text = $"{group.HostName}: в сети {onlineCount}, недоступно {offlineCount}";
            AddDeviceCheckDetailsToEventLog(group.HostName, result.Devices);
            AddEventLog($"Ручная проверка {group.HostName}: в сети {onlineCount}, недоступно {offlineCount}.");
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = $"Проверка {group.HostName} остановлена по таймауту.";
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

            existingDevice.HostName = NetworkDevice.MergeHostNames(existingDevice.HostName, device.HostName);
            existingDevice.OpenPorts = device.OpenPorts;

            if (!string.IsNullOrWhiteSpace(device.MacAddress))
            {
                existingDevice.MacAddress = device.MacAddress;
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

        foreach (var group in BuildDeviceGroups())
        {
            var rowIndex = devicesGrid.Rows.Add(
                group.TypeText,
                group.HostName,
                group.IpAddresses,
                group.MacAddresses,
                group.OpenPorts,
                group.StatusText,
                group.CheckedAtText,
                group.Source);

            var row = devicesGrid.Rows[rowIndex];
            row.Tag = group;

            if (group.IsServer)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(232, 244, 255);
                row.DefaultCellStyle.Font = _serverRowFont;
            }

            row.Cells["Status"].Style.ForeColor = group.IsOnline ? Color.ForestGreen : Color.Firebrick;
        }

        devicesGrid.ResumeLayout();
    }

    private List<NetworkDeviceGroup> BuildDeviceGroups()
    {
        return _devices.Values
            .GroupBy(GetDeviceGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(NetworkDeviceGroup.Create)
            .OrderByDescending(group => group.IsServer)
            .ThenByDescending(group => group.IsOnline)
            .ThenBy(group => NetworkScanner.ToSortableUInt32(IPAddress.Parse(group.PrimaryDevice.IpAddress)))
            .ToList();
    }

    private static string GetDeviceGroupKey(NetworkDevice device)
    {
        return device.HasKnownHostName
            ? $"name:{device.HostName.Trim()}"
            : $"ip:{device.IpAddress}";
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
        editDeviceMenuItem.Enabled = enabled;
        deleteDeviceMenuItem.Enabled = enabled;
        checkDeviceMenuItem.Enabled = enabled;
        if (!string.IsNullOrWhiteSpace(status))
        {
            statusLabel.Text = status;
        }
    }

    private static void SelectGridCellAtMouse(DataGridView grid, MouseEventArgs e)
    {
        var hit = grid.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0)
        {
            grid.ClearSelection();
            grid.CurrentCell = null;
            return;
        }

        grid.ClearSelection();
        grid.Rows[hit.RowIndex].Selected = true;
        var columnIndex = hit.ColumnIndex >= 0 ? hit.ColumnIndex : 0;
        grid.CurrentCell = grid.Rows[hit.RowIndex].Cells[columnIndex];
    }

    private static void CopyCurrentGridCell(DataGridView grid)
    {
        var text = Convert.ToString(grid.CurrentCell?.Value);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Clipboard.SetText(text);
    }

    private static void CopyGridSelection(DataGridView grid)
    {
        var dataObject = grid.GetClipboardContent();
        var text = dataObject?.GetText();

        if (string.IsNullOrEmpty(text))
        {
            text = Convert.ToString(grid.CurrentCell?.Value);
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Clipboard.SetText(text);
    }

    private void CopySelectedEventLogText()
    {
        if (eventLogListBox.SelectedItem is null)
        {
            return;
        }

        Clipboard.SetText(eventLogListBox.SelectedItem.ToString() ?? "");
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
        if (devicesGrid.CurrentRow?.Tag is not NetworkDeviceGroup group)
        {
            MessageBox.Show("Выберите строку с IP-адресом.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await CheckDeviceGroupAsync(group);
    }

    private void editDeviceMenuItem_Click(object? sender, EventArgs e)
    {
        var group = GetSelectedDeviceGroup();
        if (group is null)
        {
            return;
        }

        if (!EditDeviceGroup(group))
        {
            return;
        }

        RenderDevices();
        statusLabel.Text = "Строка изменена.";
    }

    private void deleteDeviceMenuItem_Click(object? sender, EventArgs e)
    {
        var group = GetSelectedDeviceGroup();
        if (group is null)
        {
            return;
        }

        DeleteDeviceGroup(group);
    }

    private async void checkDeviceMenuItem_Click(object? sender, EventArgs e)
    {
        var group = GetSelectedDeviceGroup();
        if (group is null)
        {
            return;
        }

        await CheckDeviceGroupAsync(group);
    }

    private async void scanNetworkButton_Click(object sender, EventArgs e)
    {
        await RunNetworkScanAsync("Ручное сканирование всей сети");
    }

    private async void devicesGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || devicesGrid.Rows[e.RowIndex].Tag is not NetworkDeviceGroup group)
        {
            return;
        }

        await CheckDeviceGroupAsync(group);
    }

    private void devicesGrid_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            SelectGridCellAtMouse(devicesGrid, e);
        }
    }

    private void dataGridView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not DataGridView grid || !e.Control || e.KeyCode != Keys.C)
        {
            return;
        }

        CopyGridSelection(grid);
        e.Handled = true;
    }

    private async void checkDefaultServersButton_Click(object? sender, EventArgs e)
    {
        await RunDefaultServerChecksAsync(_defaultServers, "Ручная проверка серверов");
    }

    private void defaultServersGrid_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        SelectGridCellAtMouse(defaultServersGrid, e);
    }

    private async void defaultServersGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || defaultServersGrid.Rows[e.RowIndex].Tag is not DefaultServerEndpoint server)
        {
            return;
        }

        await RunDefaultServerChecksAsync([server], $"Ручная проверка сервера {server.Name}");
    }

    private void addDefaultServerMenuItem_Click(object? sender, EventArgs e)
    {
        var server = new DefaultServerEndpoint
        {
            Name = "",
            Address = ""
        };

        if (!EditDefaultServer(server, "Добавить сервер"))
        {
            return;
        }

        _defaultServers.Add(server);
        SaveDefaultServers();
        RenderDefaultServers();
        SelectDefaultServerRow(server);
        AddEventLog($"Сервер добавлен: {server.Name} ({server.Address}).");
        statusLabel.Text = $"Сервер добавлен: {server.Name}.";
    }

    private void editDefaultServerMenuItem_Click(object? sender, EventArgs e)
    {
        var server = GetSelectedDefaultServer();
        if (server is null)
        {
            return;
        }

        var oldName = server.Name;
        if (!EditDefaultServer(server, "Редактировать сервер"))
        {
            return;
        }

        SaveDefaultServers();
        RenderDefaultServers();
        SelectDefaultServerRow(server);
        AddEventLog($"Сервер изменен: {oldName} -> {server.Name} ({server.Address}).");
        statusLabel.Text = $"Сервер изменен: {server.Name}.";
    }

    private void deleteDefaultServerMenuItem_Click(object? sender, EventArgs e)
    {
        var server = GetSelectedDefaultServer();
        if (server is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Удалить сервер \"{server.Name}\"?",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        _defaultServers.Remove(server);
        SaveDefaultServers();
        RenderDefaultServers();
        AddEventLog($"Сервер удален: {server.Name}.");
        statusLabel.Text = $"Сервер удален: {server.Name}.";
    }

    private async void checkDefaultServerMenuItem_Click(object? sender, EventArgs e)
    {
        var server = GetSelectedDefaultServer();
        if (server is null)
        {
            return;
        }

        await RunDefaultServerChecksAsync([server], $"Ручная проверка сервера {server.Name}");
    }

    private async void checkServicesButton_Click(object? sender, EventArgs e)
    {
        await RunServiceChecksAsync(_services);
    }

    private async void checkServiceSearchButton_Click(object? sender, EventArgs e)
    {
        await CheckServiceSearchAsync();
    }

    private async void serviceSearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
        await CheckServiceSearchAsync();
    }

    private void servicesGrid_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        SelectGridCellAtMouse(servicesGrid, e);
    }

    private void editServiceMenuItem_Click(object? sender, EventArgs e)
    {
        var service = GetSelectedService();
        if (service is null)
        {
            return;
        }

        var oldName = service.Name;
        if (!EditService(service))
        {
            return;
        }

        SaveServices();
        RenderServices();
        AddEventLog($"Сервис изменен: {oldName} -> {service.Name} ({service.Address}).");
        statusLabel.Text = $"Сервис изменен: {service.Name}.";
    }

    private void deleteServiceMenuItem_Click(object? sender, EventArgs e)
    {
        var service = GetSelectedService();
        if (service is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Удалить сервис \"{service.Name}\"?",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        _services.Remove(service);
        SaveServices();
        RenderServices();
        AddEventLog($"Сервис удален: {service.Name}.");
        statusLabel.Text = $"Сервис удален: {service.Name}.";
    }

    private async void scanServiceMenuItem_Click(object? sender, EventArgs e)
    {
        var service = GetSelectedService();
        if (service is null)
        {
            return;
        }

        await RunServiceChecksAsync([service]);
    }

    private async void servicesGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || servicesGrid.Rows[e.RowIndex].Tag is not ServiceEndpoint service)
        {
            return;
        }

        await RunServiceChecksAsync([service]);
    }

    private void eventLogListBox_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        var index = eventLogListBox.IndexFromPoint(e.Location);
        if (index == ListBox.NoMatches)
        {
            eventLogListBox.ClearSelected();
            return;
        }

        eventLogListBox.SelectedIndex = index;
    }

    private void eventLogListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Control || e.KeyCode != Keys.C)
        {
            return;
        }

        CopySelectedEventLogText();
        e.Handled = true;
    }

}
