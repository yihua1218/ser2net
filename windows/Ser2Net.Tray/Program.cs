using System.Diagnostics;
using Ser2Net.Windows.Core;

namespace Ser2Net.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly Ser2NetPaths _paths = Ser2NetPaths.FromExecutable();

    public TrayApplicationContext()
    {
        _icon = new NotifyIcon
        {
            Text = "Ser2Net",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _icon.DoubleClick += (_, _) => ShowManager();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _icon.Visible = false;
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Ser2Net Manager", null, (_, _) => ShowManager());
        menu.Items.Add("Install Service", null, (_, _) => RunServiceCommand("install"));
        menu.Items.Add("Uninstall Service", null, (_, _) => RunServiceCommand("uninstall"));
        menu.Items.Add("Start Service", null, (_, _) => RunServiceCommand("start"));
        menu.Items.Add("Stop Service", null, (_, _) => RunServiceCommand("stop"));
        menu.Items.Add("Restart Service", null, (_, _) => RunServiceCommand("restart"));
        menu.Items.Add("Reload Mapping", null, (_, _) => RunServiceCommand("generate"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Config Folder", null, (_, _) => OpenFolder(_paths.EtcDirectory));
        menu.Items.Add("Open Logs", null, (_, _) => OpenFolder(_paths.LogDirectory));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Tray App", null, (_, _) => ExitThread());
        return menu;
    }

    private void ShowManager()
    {
        using var form = new ManagerForm(_paths);
        form.ShowDialog();
    }

    private void RunServiceCommand(string command) => ManagerForm.RunServiceCommand(_paths, command);

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}

internal sealed class ManagerForm : Form
{
    private readonly Ser2NetPaths _paths;
    private readonly DataGridView _devicesGrid = new();
    private readonly DataGridView _mappingsGrid = new();
    private readonly StatusStrip _bottomStatus = new();
    private readonly ToolStripStatusLabel _serviceStatusText = new();
    private readonly ToolStripStatusLabel _mappingCountText = new();
    private readonly ToolStripStatusLabel _usbDeviceCountText = new();
    private readonly ToolStripStatusLabel _configPathText = new();
    private readonly Label _serviceState = new();
    private readonly Label _configState = new();
    private readonly ListBox _activeEndpoints = new();
    private readonly ListBox _warnings = new();
    private readonly Label _automationState = new();
    private readonly Label _validation = new();
    private readonly Label[] _workflowSteps = new Label[4];
    private readonly TextBox _name = new();
    private readonly ComboBox _matchMode = new();
    private readonly TextBox _listenAddress = new();
    private readonly NumericUpDown _tcpPort = new();
    private readonly NumericUpDown _maxConnections = new();
    private readonly ComboBox _protocol = new();
    private readonly NumericUpDown _baud = new();
    private readonly ComboBox _serialSettings = new();
    private readonly CheckBox _enabled = new();
    private readonly TextBox _banner = new();
    private readonly TextBox _identityPreview = new();
    private readonly Button _addOrUpdate = new();

    private List<SerialDevice> _devices = [];
    private MappingFile _mappingFile = new();
    private SerialDevice? _selectedDevice;
    private int _selectedMappingIndex = -1;

    public ManagerForm(Ser2NetPaths paths)
    {
        _paths = paths;
        Text = "Ser2Net Manager";
        MinimumSize = new Size(1120, 720);
        Width = 1220;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        LoadMappings();
        RefreshAll();
    }

    public static void RunServiceCommand(Ser2NetPaths paths, string command)
    {
        var serviceExe = Path.Combine(paths.BinDirectory, "Ser2Net.Service.exe");
        if (!File.Exists(serviceExe)) {
            MessageBox.Show($"Service executable not found:\n{serviceExe}", "Ser2Net", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = serviceExe,
            UseShellExecute = true
        };
        if (command is "install" or "uninstall" or "start" or "stop" or "restart") {
            psi.Verb = "runas";
        }
        psi.ArgumentList.Add(command);
        Process.Start(psi);
    }

    private void BuildLayout()
    {
        var toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8, 4, 8, 4) };
        toolbar.Items.Add(ToolButton("Refresh Devices", RefreshDevices));
        toolbar.Items.Add(ToolButton("Save", SaveAndGenerate));
        toolbar.Items.Add(ToolButton("Restart Service", SaveGenerateAndRestart));
        toolbar.Items.Add(new ToolStripSeparator());
        var tools = new ToolStripDropDownButton("Tools");
        tools.DropDownItems.Add("Generate Config", null, (_, _) => SaveAndGenerate());
        tools.DropDownItems.Add("Open Config Folder", null, (_, _) => OpenFolder(_paths.EtcDirectory));
        tools.DropDownItems.Add("Open Logs", null, (_, _) => OpenFolder(_paths.LogDirectory));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add("Clear All Mappings", null, (_, _) => ClearAllMappings());
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add("Install Service", null, (_, _) => RunServiceCommand(_paths, "install"));
        tools.DropDownItems.Add("Uninstall Service", null, (_, _) => RunServiceCommand(_paths, "uninstall"));
        toolbar.Items.Add(tools);

        var workflow = BuildWorkflowStrip();

        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(8)
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        workspace.Controls.Add(BuildDevicesPanel(), 0, 0);
        workspace.Controls.Add(BuildMappingsPanel(), 1, 0);
        workspace.Controls.Add(BuildServiceStatusPanel(), 2, 0);

        BuildBottomStatusBar();

        Controls.Add(workspace);
        Controls.Add(_bottomStatus);
        Controls.Add(workflow);
        Controls.Add(toolbar);
    }

    private Control BuildWorkflowStrip()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            ColumnCount = 4,
            Padding = new Padding(8, 4, 8, 4),
            BackColor = SystemColors.ControlLight
        };

        var labels = new[]
        {
            "1  Detect USB Devices",
            "2  Create / Edit Mapping",
            "3  Save Configuration",
            "4  Restart Service"
        };

        for (var i = 0; i < labels.Length; i++) {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var label = new Label
            {
                Text = labels[i],
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = i == 0 ? Color.FromArgb(230, 245, 255) : SystemColors.Window,
                ForeColor = SystemColors.ControlText
            };
            _workflowSteps[i] = label;
            panel.Controls.Add(label, i, 0);
        }

        return panel;
    }

    private void BuildBottomStatusBar()
    {
        _bottomStatus.Dock = DockStyle.Bottom;
        _bottomStatus.SizingGrip = false;
        _serviceStatusText.Text = "Service: Unknown";
        _mappingCountText.Text = "Mappings: 0";
        _usbDeviceCountText.Text = "USB Devices: 0";
        _configPathText.Text = $"Config: {_paths.GeneratedConfigPath}";
        _configPathText.Spring = true;
        _configPathText.TextAlign = ContentAlignment.MiddleLeft;
        _configPathText.ToolTipText = _paths.GeneratedConfigPath;
        _bottomStatus.Items.Add(_serviceStatusText);
        _bottomStatus.Items.Add(new ToolStripStatusLabel { Text = "|" });
        _bottomStatus.Items.Add(_mappingCountText);
        _bottomStatus.Items.Add(new ToolStripStatusLabel { Text = "|" });
        _bottomStatus.Items.Add(_usbDeviceCountText);
        _bottomStatus.Items.Add(new ToolStripStatusLabel { Text = "|" });
        _bottomStatus.Items.Add(_configPathText);
    }

    private Control BuildDevicesPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6), BorderStyle = BorderStyle.FixedSingle };
        var title = new Label { Dock = DockStyle.Top, Height = 30, Text = "Detected USB Devices", Font = new Font(Font, FontStyle.Bold) };
        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            Text = "Double-click or drag a device into the mapping list to create a default mapping.",
            ForeColor = SystemColors.GrayText
        };

        ConfigureGrid(_devicesGrid);
        _devicesGrid.Columns.Add("Port", "COM");
        _devicesGrid.Columns.Add("Name", "Device");
        _devicesGrid.Columns.Add("Usb", "VID/PID");
        _devicesGrid.Columns.Add("Serial", "Serial");
        _devicesGrid.Columns.Add("Location", "USB Location");
        _devicesGrid.SelectionChanged += (_, _) => SelectDeviceFromGrid();
        _devicesGrid.CellDoubleClick += (_, _) => MapSelectedDeviceByLocation();
        _devicesGrid.MouseDown += DevicesGridMouseDown;

        panel.Controls.Add(_devicesGrid);
        panel.Controls.Add(hint);
        panel.Controls.Add(title);
        return panel;
    }

    private Control BuildMappingsPanel()
    {
        var panel = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 310,
            BorderStyle = BorderStyle.FixedSingle
        };

        var mappingsPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var title = new Label { Dock = DockStyle.Top, Height = 30, Text = "Mapping Configuration Editor", Font = new Font(Font, FontStyle.Bold) };
        ConfigureGrid(_mappingsGrid);
        _mappingsGrid.Columns.Add("Status", "Status");
        _mappingsGrid.Columns.Add("Device", "Device");
        _mappingsGrid.Columns.Add("Com", "COM");
        _mappingsGrid.Columns.Add("SerialNumber", "Serial Number");
        _mappingsGrid.Columns.Add("TcpPort", "TCP Port");
        _mappingsGrid.Columns.Add("Protocol", "Protocol");
        _mappingsGrid.Columns.Add("Enabled", "Enabled");
        _mappingsGrid.Columns.Add("Name", "Alias");
        _mappingsGrid.Columns.Add("Serial", "Serial");
        _mappingsGrid.SelectionChanged += (_, _) => SelectMappingFromGrid();
        _mappingsGrid.AllowDrop = true;
        _mappingsGrid.DragEnter += MappingsGridDragEnter;
        _mappingsGrid.DragDrop += MappingsGridDragDrop;
        mappingsPanel.Controls.Add(_mappingsGrid);
        mappingsPanel.Controls.Add(title);

        panel.Panel1.Controls.Add(mappingsPanel);
        panel.Panel2.Controls.Add(BuildEditorPanel());
        return panel;
    }

    private Control BuildServiceStatusPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6),
            BorderStyle = BorderStyle.FixedSingle,
            ColumnCount = 1,
            RowCount = 8
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

        panel.Controls.Add(new Label { Text = "Active Service Status", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        panel.Controls.Add(_serviceState, 0, 1);
        panel.Controls.Add(_configState, 0, 2);
        panel.Controls.Add(new Label { Text = "Active endpoints", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText }, 0, 3);

        _activeEndpoints.Dock = DockStyle.Fill;
        _activeEndpoints.IntegralHeight = false;
        panel.Controls.Add(_activeEndpoints, 0, 4);

        panel.Controls.Add(new Label { Text = "Warnings", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText }, 0, 5);
        _warnings.Dock = DockStyle.Fill;
        _warnings.IntegralHeight = false;
        panel.Controls.Add(_warnings, 0, 6);

        _automationState.Dock = DockStyle.Fill;
        _automationState.BorderStyle = BorderStyle.FixedSingle;
        _automationState.Padding = new Padding(8);
        _automationState.Text = "Automation\nReserved for AI generated mapping, auto port assignment, conflict detection, MCP registration, and MCP export.";
        _automationState.ForeColor = SystemColors.GrayText;
        panel.Controls.Add(_automationState, 0, 7);

        return panel;
    }

    private Control BuildEditorPanel()
    {
        var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var title = new Label { Dock = DockStyle.Top, Height = 28, Text = "Selected Mapping", Font = new Font(Font, FontStyle.Bold) };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 4, 0, 0),
            ColumnCount = 4,
            RowCount = 9
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _matchMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _matchMode.Items.AddRange(["usb-location", "usb-serial", "com-name"]);
        _matchMode.SelectedIndex = 0;
        _matchMode.SelectedIndexChanged += (_, _) => UpdateIdentityPreview();

        _protocol.DropDownStyle = ComboBoxStyle.DropDownList;
        _protocol.Items.AddRange(["telnet", "tcp"]);
        _protocol.SelectedIndex = 0;

        _listenAddress.Text = "0.0.0.0";
        _tcpPort.Minimum = 1;
        _tcpPort.Maximum = 65535;
        _tcpPort.Value = 3001;

        _maxConnections.Minimum = 1;
        _maxConnections.Maximum = 1000;
        _maxConnections.Value = 10;

        _baud.Minimum = 300;
        _baud.Maximum = 4000000;
        _baud.Value = 115200;
        _baud.Increment = 300;

        _serialSettings.DropDownStyle = ComboBoxStyle.DropDown;
        _serialSettings.Items.AddRange(["N81", "E81", "O81", "N82", "N71"]);
        _serialSettings.Text = "N81";

        _enabled.Text = "Enabled";
        _enabled.Checked = true;

        _banner.Multiline = true;
        _banner.ScrollBars = ScrollBars.Vertical;
        _banner.Height = 64;

        _identityPreview.Multiline = true;
        _identityPreview.ReadOnly = true;
        _identityPreview.ScrollBars = ScrollBars.Vertical;
        _identityPreview.Height = 64;

        _validation.Dock = DockStyle.Fill;
        _validation.ForeColor = Color.DarkRed;
        _validation.AutoEllipsis = true;

        _addOrUpdate.Text = "Add Mapping";
        _addOrUpdate.AutoSize = true;
        _addOrUpdate.Click += (_, _) => AddOrUpdateMapping();

        _name.TextChanged += (_, _) => ValidateEditor();
        _listenAddress.TextChanged += (_, _) => ValidateEditor();
        _tcpPort.ValueChanged += (_, _) => ValidateEditor();
        _maxConnections.ValueChanged += (_, _) => ValidateEditor();
        _baud.ValueChanged += (_, _) => ValidateEditor();
        _protocol.SelectedIndexChanged += (_, _) => ValidateEditor();
        _serialSettings.TextChanged += (_, _) => ValidateEditor();

        AddRow(panel, 0, "Alias", _name, "Match by", _matchMode);
        AddRow(panel, 1, "Listen IP", _listenAddress, "TCP Port", _tcpPort);
        AddRow(panel, 2, "Max clients", _maxConnections, "Protocol", _protocol);
        AddRow(panel, 3, "Baud", _baud, "Serial", _serialSettings);
        AddRow(panel, 4, "", _enabled, "", new Label());
        panel.Controls.Add(new Label { Text = "Banner", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
        panel.SetColumnSpan(_banner, 3);
        panel.Controls.Add(_banner, 1, 5);
        panel.Controls.Add(new Label { Text = "Selected identity", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
        panel.SetColumnSpan(_identityPreview, 3);
        panel.Controls.Add(_identityPreview, 1, 6);
        panel.Controls.Add(new Label { Text = "Validation", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 7);
        panel.SetColumnSpan(_validation, 3);
        panel.Controls.Add(_validation, 1, 7);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(_addOrUpdate);
        buttons.Controls.Add(Button("Delete Mapping", DeleteSelectedMapping));
        buttons.Controls.Add(Button("Clear", ClearEditor));
        panel.SetColumnSpan(buttons, 4);
        panel.Controls.Add(buttons, 0, 8);

        outer.Controls.Add(panel);
        outer.Controls.Add(title);
        return outer;
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label1, Control control1, string label2, Control control2)
    {
        panel.Controls.Add(new Label { Text = label1, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        panel.Controls.Add(control1, 1, row);
        panel.Controls.Add(new Label { Text = label2, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, row);
        panel.Controls.Add(control2, 3, row);
        control1.Dock = DockStyle.Fill;
        control2.Dock = DockStyle.Fill;
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 28 };
        button.Click += (_, _) => action();
        return button;
    }

    private static ToolStripButton ToolButton(string text, Action action)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    }

    private void RefreshAll()
    {
        RefreshDevices();
        RefreshMappings();
        RefreshStatus();
    }

    private void LoadMappings()
    {
        _mappingFile = MappingStore.LoadOrCreate(_paths.MappingFilePath);
    }

    private void RefreshDevices()
    {
        try {
            _devices = SerialPortEnumerator.Enumerate().ToList();
        }
        catch (Exception ex) {
            MessageBox.Show($"Device scan failed:\n{ex.Message}", "Ser2Net", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _devices = [];
        }

        _devicesGrid.Rows.Clear();
        foreach (var device in _devices) {
            var row = _devicesGrid.Rows[_devicesGrid.Rows.Add(
                device.PortName,
                DeviceDisplayName(device),
                $"{device.Vid}/{device.Pid}".Trim('/'),
                device.UsbSerial,
                device.LocationPath)];
            row.Tag = device;
        }

        RefreshMappings();
        RefreshStatus();
    }

    private void RefreshMappings()
    {
        var resolved = MappingResolver.Resolve(_mappingFile.Mappings, _devices).ToDictionary(r => r.Mapping);
        _mappingsGrid.Rows.Clear();
        for (var i = 0; i < _mappingFile.Mappings.Count; i++) {
            var mapping = _mappingFile.Mappings[i];
            resolved.TryGetValue(mapping, out var resolvedMapping);
            var status = GetMappingStatus(mapping, resolvedMapping);
            var row = _mappingsGrid.Rows[_mappingsGrid.Rows.Add(
                status,
                resolvedMapping is null ? DeviceNameFromMatch(mapping.Match) : DeviceDisplayName(resolvedMapping.Device),
                resolvedMapping?.Device.PortName ?? "",
                resolvedMapping?.Device.UsbSerial ?? mapping.Match.SerialNumber,
                mapping.Tcp.Port,
                mapping.Tcp.Mode,
                mapping.Enabled ? "Yes" : "No",
                mapping.Name,
                $"{mapping.Serial.Baud}{mapping.Serial.Settings}")];
            row.Tag = i;
            ApplyMappingStatusStyle(row, status);
        }
    }

    private void RefreshStatus()
    {
        var resolvedCount = MappingResolver.Resolve(_mappingFile.Mappings, _devices).Count;
        var serviceState = GetServiceState();
        _serviceStatusText.Text = $"Service: {NormalizeServiceState(serviceState)}";
        _mappingCountText.Text = $"Mappings: {_mappingFile.Mappings.Count}";
        _usbDeviceCountText.Text = $"USB Devices: {_devices.Count}";
        _configPathText.Text = $"Config: {EllipsizePath(_paths.GeneratedConfigPath, 72)}";
        _configPathText.ToolTipText = _paths.GeneratedConfigPath;

        _serviceState.Text = $"Service: {NormalizeServiceState(serviceState)}";
        _serviceState.ForeColor = serviceState.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ? Color.DarkGreen : Color.DarkRed;
        _configState.Text = $"Mappings loaded: {resolvedCount}/{_mappingFile.Mappings.Count}";

        _activeEndpoints.Items.Clear();
        foreach (var resolved in MappingResolver.Resolve(_mappingFile.Mappings, _devices)) {
            _activeEndpoints.Items.Add($"{resolved.Mapping.Tcp.Port}  {resolved.Mapping.Name}  {resolved.Mapping.Tcp.Mode}  {resolved.Device.PortName}");
        }
        if (_activeEndpoints.Items.Count == 0) {
            _activeEndpoints.Items.Add("No active resolved endpoints");
        }

        _warnings.Items.Clear();
        foreach (var warning in GetWarnings()) {
            _warnings.Items.Add(warning);
        }
        if (_warnings.Items.Count == 0) {
            _warnings.Items.Add("No warnings");
        }

        UpdateWorkflowState(resolvedCount);
    }

    private void SelectDeviceFromGrid()
    {
        _selectedDevice = _devicesGrid.SelectedRows.Count == 0 ? null : _devicesGrid.SelectedRows[0].Tag as SerialDevice;
        if (_selectedDevice is null) {
            return;
        }

        if (_selectedMappingIndex < 0) {
            PopulateDefaultsFromDevice(_selectedDevice);
        }
        UpdateIdentityPreview();
        ValidateEditor();
    }

    private void SelectMappingFromGrid()
    {
        if (_mappingsGrid.SelectedRows.Count == 0 || _mappingsGrid.SelectedRows[0].Tag is not int index || index < 0 || index >= _mappingFile.Mappings.Count) {
            return;
        }

        _selectedMappingIndex = index;
        var mapping = _mappingFile.Mappings[index];
        _name.Text = mapping.Name;
        _enabled.Checked = mapping.Enabled;
        _matchMode.SelectedItem = mapping.Match.Mode;
        _listenAddress.Text = mapping.Tcp.ListenAddress;
        _protocol.SelectedItem = mapping.Tcp.Mode;
        _tcpPort.Value = Math.Clamp(mapping.Tcp.Port, 1, 65535);
        _maxConnections.Value = Math.Clamp(mapping.Tcp.MaxConnections, 1, 1000);
        _baud.Value = Math.Clamp(mapping.Serial.Baud, 300, 4000000);
        _serialSettings.Text = mapping.Serial.Settings;
        _banner.Text = mapping.Banner;
        _addOrUpdate.Text = "Update Mapping";
        UpdateIdentityPreview(mapping.Match);
        ValidateEditor();
    }

    private void MapSelectedDeviceByLocation()
    {
        if (_selectedDevice is null) {
            MessageBox.Show("Select a detected USB / COM console line first.", "Ser2Net");
            return;
        }

        PopulateDefaultsFromDevice(_selectedDevice);
        _matchMode.SelectedItem = !string.IsNullOrWhiteSpace(_selectedDevice.LocationPath)
            ? "usb-location"
            : !string.IsNullOrWhiteSpace(_selectedDevice.UsbSerial)
                ? "usb-serial"
                : "com-name";
        AddOrUpdateMapping();
    }

    private void PopulateDefaultsFromDevice(SerialDevice device)
    {
        _selectedMappingIndex = -1;
        _addOrUpdate.Text = "Add Mapping";
        _enabled.Checked = true;
        _name.Text = DefaultAlias(device);
        _listenAddress.Text = "0.0.0.0";
        _protocol.SelectedItem = "telnet";
        _tcpPort.Value = NextTcpPort();
        _maxConnections.Value = 10;
        _baud.Value = 115200;
        _serialSettings.Text = "N81";
        _banner.Text = "";
    }

    private void AddOrUpdateMapping()
    {
        if (_selectedDevice is null && _selectedMappingIndex < 0) {
            MessageBox.Show("Select a detected USB / COM console line first.", "Ser2Net");
            return;
        }

        if (!ValidateEditor()) {
            MessageBox.Show(_validation.Text, "Ser2Net", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var device = _selectedDevice;
        var mapping = _selectedMappingIndex >= 0
            ? _mappingFile.Mappings[_selectedMappingIndex]
            : new PortMapping();

        mapping.Name = string.IsNullOrWhiteSpace(_name.Text) ? "console" : _name.Text.Trim();
        mapping.Enabled = _enabled.Checked;
        mapping.Match = device is null ? mapping.Match : BuildMatch(device);
        mapping.Tcp = new TcpEndpoint
        {
            Mode = (_protocol.SelectedItem?.ToString() ?? "telnet").ToLowerInvariant(),
            ListenAddress = string.IsNullOrWhiteSpace(_listenAddress.Text) ? "0.0.0.0" : _listenAddress.Text.Trim(),
            Port = (int)_tcpPort.Value,
            MaxConnections = (int)_maxConnections.Value
        };
        mapping.Serial = new SerialSettings
        {
            Baud = (int)_baud.Value,
            Settings = string.IsNullOrWhiteSpace(_serialSettings.Text) ? "N81" : _serialSettings.Text.Trim().ToUpperInvariant()
        };
        mapping.Banner = _banner.Text.Trim();

        if (_selectedMappingIndex < 0) {
            _mappingFile.Mappings.Add(mapping);
            _selectedMappingIndex = _mappingFile.Mappings.Count - 1;
        }

        MappingStore.Save(_paths.MappingFilePath, _mappingFile);
        RefreshMappings();
        RefreshStatus();
        ValidateEditor();
    }

    private DeviceMatch BuildMatch(SerialDevice device)
    {
        var mode = _matchMode.SelectedItem?.ToString() ?? "usb-location";
        return mode switch
        {
            "usb-serial" => new DeviceMatch
            {
                Mode = "usb-serial",
                Vid = device.Vid,
                Pid = device.Pid,
                SerialNumber = device.UsbSerial,
                Interface = ParseInterface(device)
            },
            "com-name" => new DeviceMatch
            {
                Mode = "com-name",
                PortName = device.PortName
            },
            _ => new DeviceMatch
            {
                Mode = "usb-location",
                Vid = device.Vid,
                Pid = device.Pid,
                LocationPath = device.LocationPath,
                Interface = ParseInterface(device)
            }
        };
    }

    private void DeleteSelectedMapping()
    {
        if (_selectedMappingIndex < 0 || _selectedMappingIndex >= _mappingFile.Mappings.Count) {
            return;
        }

        _mappingFile.Mappings.RemoveAt(_selectedMappingIndex);
        _selectedMappingIndex = -1;
        MappingStore.Save(_paths.MappingFilePath, _mappingFile);
        ClearEditor();
        RefreshMappings();
        RefreshStatus();
    }

    private void ClearEditor()
    {
        _selectedMappingIndex = -1;
        _selectedDevice = null;
        _name.Clear();
        _matchMode.SelectedItem = "usb-location";
        _listenAddress.Text = "0.0.0.0";
        _protocol.SelectedItem = "telnet";
        _tcpPort.Value = NextTcpPort();
        _maxConnections.Value = 10;
        _baud.Value = 115200;
        _serialSettings.Text = "N81";
        _enabled.Checked = true;
        _banner.Clear();
        _identityPreview.Clear();
        _validation.Text = "";
        _addOrUpdate.Text = "Add Mapping";
    }

    private void ClearAllMappings()
    {
        var result = MessageBox.Show(
            "Clear all mappings? This removes every fixed USB-to-service-port mapping from mappings.json.",
            "Ser2Net",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) {
            return;
        }

        _mappingFile.Mappings.Clear();
        _selectedMappingIndex = -1;
        MappingStore.Save(_paths.MappingFilePath, _mappingFile);
        ClearEditor();
        RefreshMappings();
        RefreshStatus();
    }

    private void SaveAndGenerate()
    {
        if (!ValidateAllMappings()) {
            MessageBox.Show(_validation.Text, "Ser2Net", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MappingStore.Save(_paths.MappingFilePath, _mappingFile);
        var resolved = MappingResolver.Resolve(_mappingFile.Mappings, SerialPortEnumerator.Enumerate());
        Ser2NetConfigGenerator.WriteConfig(_paths.GeneratedConfigPath, resolved);
        RefreshDevices();
        MessageBox.Show($"Generated config with {resolved.Count} resolved mapping(s).", "Ser2Net");
    }

    private void SaveGenerateAndRestart()
    {
        SaveAndGenerate();
        RunServiceCommand(_paths, "restart");
    }

    private void UpdateIdentityPreview(DeviceMatch? match = null)
    {
        if (match is not null) {
            _identityPreview.Text = MatchSummary(match);
            return;
        }

        if (_selectedDevice is null) {
            _identityPreview.Clear();
            return;
        }

        _identityPreview.Text = MatchSummary(BuildMatch(_selectedDevice));
    }

    private bool ValidateEditor()
    {
        if (_name.IsDisposed) {
            return true;
        }

        var errors = new List<string>();
        var alias = _name.Text.Trim();
        if (string.IsNullOrWhiteSpace(alias)) {
            errors.Add("Alias is required.");
        }
        else if (!alias.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')) {
            errors.Add("Alias can only use letters, numbers, dash, underscore, or dot.");
        }

        var port = (int)_tcpPort.Value;
        var conflictingMapping = _mappingFile.Mappings
            .Select((mapping, index) => new { mapping, index })
            .FirstOrDefault(item => item.index != _selectedMappingIndex && item.mapping.Enabled && item.mapping.Tcp.Port == port);
        if (conflictingMapping is not null) {
            errors.Add($"TCP port {port} is already used by {conflictingMapping.mapping.Name}.");
        }

        if (_selectedMappingIndex < 0 && _selectedDevice is null) {
            errors.Add("Select or drag a USB/COM device before creating a mapping.");
        }

        if (_selectedDevice is not null) {
            var match = BuildMatch(_selectedDevice);
            if (match.Mode == "usb-location" && string.IsNullOrWhiteSpace(match.LocationPath)) {
                errors.Add("USB location is unavailable; use USB serial or COM-name fallback.");
            }
            if (match.Mode == "usb-serial" && string.IsNullOrWhiteSpace(match.SerialNumber)) {
                errors.Add("USB serial number is unavailable; use USB location or COM-name fallback.");
            }
        }

        _validation.Text = errors.Count == 0 ? "Ready." : string.Join(" ", errors);
        _validation.ForeColor = errors.Count == 0 ? Color.DarkGreen : Color.DarkRed;
        return errors.Count == 0;
    }

    private bool ValidateAllMappings()
    {
        var errors = new List<string>();
        var enabled = _mappingFile.Mappings.Where(m => m.Enabled).ToList();
        foreach (var group in enabled.GroupBy(m => m.Tcp.Port).Where(g => g.Count() > 1)) {
            errors.Add($"TCP port {group.Key} is used by: {string.Join(", ", group.Select(m => m.Name))}.");
        }

        foreach (var mapping in _mappingFile.Mappings) {
            if (string.IsNullOrWhiteSpace(mapping.Name)) {
                errors.Add("Every mapping must have an alias.");
            }
        }

        if (errors.Count == 0) {
            return ValidateEditor();
        }

        _validation.Text = string.Join(" ", errors);
        _validation.ForeColor = Color.DarkRed;
        return false;
    }

    private IEnumerable<string> GetWarnings()
    {
        foreach (var group in _mappingFile.Mappings.Where(m => m.Enabled).GroupBy(m => m.Tcp.Port).Where(g => g.Count() > 1)) {
            yield return $"Duplicate TCP port {group.Key}";
        }

        var resolved = MappingResolver.Resolve(_mappingFile.Mappings, _devices).Select(r => r.Mapping).ToHashSet();
        foreach (var mapping in _mappingFile.Mappings.Where(m => m.Enabled && !resolved.Contains(m))) {
            yield return $"{mapping.Name}: configured but not active";
        }
    }

    private string GetMappingStatus(PortMapping mapping, ResolvedMapping? resolved)
    {
        if (!mapping.Enabled) {
            return "Configured";
        }

        if (_mappingFile.Mappings.Any(other => !ReferenceEquals(other, mapping) && other.Enabled && other.Tcp.Port == mapping.Tcp.Port)) {
            return "Error";
        }

        if (resolved is null) {
            return "Configured";
        }

        var service = GetServiceState();
        return service.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ? "Running" : "Configured";
    }

    private static void ApplyMappingStatusStyle(DataGridViewRow row, string status)
    {
        var color = status switch
        {
            "Running" => Color.FromArgb(226, 247, 232),
            "Configured" => Color.FromArgb(255, 248, 220),
            "Error" => Color.FromArgb(255, 230, 230),
            _ => SystemColors.Window
        };
        row.DefaultCellStyle.BackColor = color;
    }

    private void UpdateWorkflowState(int resolvedCount)
    {
        var completed = new[]
        {
            _devices.Count > 0,
            _mappingFile.Mappings.Count > 0,
            File.Exists(_paths.GeneratedConfigPath),
            GetServiceState().Contains("RUNNING", StringComparison.OrdinalIgnoreCase) && resolvedCount > 0
        };

        for (var i = 0; i < _workflowSteps.Length; i++) {
            if (_workflowSteps[i] is null) {
                continue;
            }

            _workflowSteps[i].BackColor = completed[i] ? Color.FromArgb(226, 247, 232) : i == Array.IndexOf(completed, false) ? Color.FromArgb(230, 245, 255) : SystemColors.Window;
        }
    }

    private void DevicesGridMouseDown(object? sender, MouseEventArgs e)
    {
        var hit = _devicesGrid.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0) {
            return;
        }

        _devicesGrid.ClearSelection();
        _devicesGrid.Rows[hit.RowIndex].Selected = true;
        _selectedDevice = _devicesGrid.Rows[hit.RowIndex].Tag as SerialDevice;
        if (_selectedDevice is not null) {
            _devicesGrid.DoDragDrop(_selectedDevice, DragDropEffects.Copy);
        }
    }

    private void MappingsGridDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(SerialDevice)) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void MappingsGridDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(SerialDevice)) is not SerialDevice device) {
            return;
        }

        _selectedDevice = device;
        PopulateDefaultsFromDevice(device);
        MapSelectedDeviceByLocation();
    }

    private int NextTcpPort()
    {
        var used = _mappingFile.Mappings.Select(m => m.Tcp.Port).ToHashSet();
        for (var port = 3001; port < 65535; port++) {
            if (!used.Contains(port)) {
                return port;
            }
        }
        return 3001;
    }

    private static string DeviceDisplayName(SerialDevice device) =>
        string.IsNullOrWhiteSpace(device.FriendlyName) ? device.Manufacturer : device.FriendlyName;

    private static string DeviceNameFromMatch(DeviceMatch match) =>
        match.Mode switch
        {
            "usb-serial" => $"USB {match.Vid}/{match.Pid}",
            "usb-location" => $"USB {match.Vid}/{match.Pid}",
            "com-name" => match.PortName,
            _ => match.Mode
        };

    private static string DefaultAlias(SerialDevice device)
    {
        var baseName = string.IsNullOrWhiteSpace(device.PortName) ? "console" : device.PortName.ToLowerInvariant();
        return $"{baseName}-console";
    }

    private static string MatchSummary(DeviceMatch match) => match.Mode switch
    {
        "usb-location" => $"USB location | VID/PID {match.Vid}/{match.Pid} | {match.LocationPath}",
        "usb-serial" => $"USB serial | VID/PID {match.Vid}/{match.Pid} | serial {match.SerialNumber}",
        "com-name" => $"COM name | {match.PortName}",
        _ => match.Mode
    };

    private static string ParseInterface(SerialDevice device)
    {
        var parts = device.DeviceInstanceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.FirstOrDefault(p => p.StartsWith("MI_", StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    private static string GetServiceState()
    {
        try {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("query");
            psi.ArgumentList.Add("Ser2Net");
            using var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd() ?? "";
            process?.WaitForExit(2000);

            foreach (var line in output.Split(Environment.NewLine)) {
                if (line.TrimStart().StartsWith("STATE", StringComparison.OrdinalIgnoreCase)) {
                    return line.Trim();
                }
            }

            return process?.ExitCode == 0 ? "Installed" : "Not installed";
        }
        catch {
            return "Not installed";
        }
    }

    private static string NormalizeServiceState(string state)
    {
        if (state.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) {
            return "Running";
        }
        if (state.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) {
            return "Stopped";
        }
        if (state.Contains("Not installed", StringComparison.OrdinalIgnoreCase)) {
            return "Not installed";
        }
        return state;
    }

    private static string EllipsizePath(string path, int maxLength)
    {
        if (path.Length <= maxLength) {
            return path;
        }

        var keep = Math.Max(10, (maxLength - 3) / 2);
        return $"{path[..keep]}...{path[^keep..]}";
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}
