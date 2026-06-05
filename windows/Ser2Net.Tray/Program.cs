using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Ser2Net.Windows.Core;

namespace Ser2Net.Tray;

internal static class Program
{
    private const string SingleInstanceMutexName = "Global\\Ser2Net.Tray.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        // A named mutex prevents duplicate tray/manager processes. Later launches notify the first process through a named pipe.
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var ownsMutex);
        if (!ownsMutex) {
            TrayApplicationContext.ForwardActivation(args);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal static class AppIcons
{
    public static Icon CreateApplicationIcon()
    {
        var executablePath = Environment.ProcessPath ?? Application.ExecutablePath;
        if (File.Exists(executablePath)) {
            var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is not null) {
                return icon;
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string ActivationPipeName = "Ser2Net.Tray.Activation";

    private readonly NotifyIcon _icon;
    private readonly Ser2NetPaths _paths = Ser2NetPaths.FromExecutable();
    private readonly CancellationTokenSource _activationListenerCts = new();
    private readonly SynchronizationContext _uiContext;
    private ManagerForm? _managerForm;

    public TrayApplicationContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _icon = new NotifyIcon
        {
            Text = "Ser2Net",
            Icon = AppIcons.CreateApplicationIcon(),
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _icon.DoubleClick += (_, _) => ShowManager();
        StartActivationListener();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _activationListenerCts.Cancel();
            _activationListenerCts.Dispose();
            _managerForm?.Dispose();
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
        if (_managerForm is { IsDisposed: false }) {
            _managerForm.FocusExistingWindow();
            return;
        }

        _managerForm = new ManagerForm(_paths);
        _managerForm.FormClosed += (_, _) => _managerForm = null;
        _managerForm.Show();
        _managerForm.FocusExistingWindow();
    }

    private void RunServiceCommand(string command) => ManagerForm.RunServiceCommand(_paths, command);

    public static void ForwardActivation(string[] args)
    {
        try {
            using var client = new NamedPipeClientStream(".", ActivationPipeName, PipeDirection.Out);
            client.Connect(500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(string.Join('\t', args));
        }
        catch {
            // The first instance owns the mutex but may still be starting. Failing quietly keeps the second launch from opening a duplicate UI.
        }
    }

    private void StartActivationListener()
    {
        _ = Task.Run(async () =>
        {
            while (!_activationListenerCts.IsCancellationRequested) {
                try {
                    using var server = new NamedPipeServerStream(ActivationPipeName, PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_activationListenerCts.Token).ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    _ = await reader.ReadLineAsync(_activationListenerCts.Token).ConfigureAwait(false);
                    _uiContext.Post(_ => ShowManager(), null);
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch {
                    await Task.Delay(250, _activationListenerCts.Token).ConfigureAwait(false);
                }
            }
        }, _activationListenerCts.Token);
    }

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
    private readonly Label _devicesEmptyState = new();
    private readonly Label _mappingsEmptyState = new();
    private readonly StatusStrip _bottomStatus = new();
    private readonly ToolStripStatusLabel _serviceStatusText = new();
    private readonly ToolStripStatusLabel _mappingCountText = new();
    private readonly ToolStripStatusLabel _usbDeviceCountText = new();
    private readonly ToolStripStatusLabel _operationStatusText = new();
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
    private readonly CheckBox _serialLocal = new();
    private readonly CheckBox _serialNoBreak = new();
    private readonly CheckBox _serialRtsCts = new();
    private readonly TextBox _banner = new();
    private readonly TextBox _identityPreview = new();
    private readonly Button _addOrUpdate = new();
    private readonly Button _deleteMapping = new();
    private readonly Button _deleteMappingFromList = new();
    private readonly ErrorProvider _errors = new();
    private ToolStripButton _refreshButton = null!;
    private ToolStripButton _saveButton = null!;
    private ToolStripButton _restartButton = null!;
    private bool _isRefreshing;
    private bool _isSaving;
    private bool _isRestarting;
    private bool _editorIsValid = true;

    private List<SerialDevice> _devices = [];
    private MappingFile _mappingFile = new();
    private SerialDevice? _selectedDevice;
    private int _selectedMappingIndex = -1;

    public ManagerForm(Ser2NetPaths paths)
    {
        _paths = paths;
        Text = "Ser2Net Manager";
        Icon = AppIcons.CreateApplicationIcon();
        MinimumSize = new Size(1280, 720);
        Width = 1440;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        _errors.ContainerControl = this;

        BuildLayout();
        LoadMappings();
        RefreshAll();
    }

    public void FocusExistingWindow()
    {
        if (WindowState == FormWindowState.Minimized) {
            WindowState = FormWindowState.Normal;
        }

        Show();
        Activate();
        BringToFront();
        NativeMethods.ShowWindow(Handle, NativeMethods.SwRestore);
        NativeMethods.SetForegroundWindow(Handle);
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
        _refreshButton = ToolButton("Refresh Devices", () => _ = RefreshDevicesAsync());
        _saveButton = ToolButton("Save", () => _ = SaveAndGenerateAsync(showSuccess: true));
        _restartButton = ToolButton("Restart Service", () => _ = SaveGenerateAndRestartAsync());
        toolbar.Items.Add(_refreshButton);
        toolbar.Items.Add(_saveButton);
        toolbar.Items.Add(_restartButton);
        toolbar.Items.Add(new ToolStripSeparator());
        var tools = new ToolStripDropDownButton("Tools");
        tools.DropDownItems.Add("Generate Config", null, (_, _) => _ = SaveAndGenerateAsync(showSuccess: true));
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
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
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
            Height = 34,
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
                BorderStyle = BorderStyle.None,
                Padding = new Padding(6, 2, 6, 2),
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
        _operationStatusText.Text = "Ready";
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
        _bottomStatus.Items.Add(_operationStatusText);
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
        SetColumnWidths(_devicesGrid, ("Port", 64), ("Name", 180), ("Usb", 92), ("Serial", 120), ("Location", 240));
        _devicesGrid.SelectionChanged += (_, _) => SelectDeviceFromGrid();
        _devicesGrid.CellDoubleClick += (_, _) => MapSelectedDeviceByLocation();
        _devicesGrid.MouseDown += DevicesGridMouseDown;

        ConfigureEmptyState(_devicesEmptyState, "No USB serial devices detected. Click Refresh Devices.");

        panel.Controls.Add(_devicesEmptyState);
        panel.Controls.Add(_devicesGrid);
        panel.Controls.Add(hint);
        panel.Controls.Add(title);
        return panel;
    }

    private Control BuildMappingsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BorderStyle = BorderStyle.FixedSingle
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 56));

        var mappingsPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            ColumnCount = 2,
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Mapping Configuration Editor", TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) }, 0, 0);

        _deleteMappingFromList.Text = "Delete Selected";
        _deleteMappingFromList.AutoSize = true;
        _deleteMappingFromList.Enabled = false;
        _deleteMappingFromList.Click += (_, _) => DeleteSelectedMapping();
        header.Controls.Add(_deleteMappingFromList, 1, 0);

        ConfigureGrid(_mappingsGrid);
        _mappingsGrid.Columns.Add("Status", "Status");
        _mappingsGrid.Columns.Add("Com", "COM");
        _mappingsGrid.Columns.Add("TcpPort", "TCP");
        _mappingsGrid.Columns.Add("Protocol", "Protocol");
        _mappingsGrid.Columns.Add("Enabled", "Enabled");
        _mappingsGrid.Columns.Add("Name", "Alias");
        SetColumnWidths(_mappingsGrid, ("Status", 92), ("Com", 64), ("TcpPort", 72), ("Protocol", 86), ("Enabled", 72), ("Name", 180));
        _mappingsGrid.SelectionChanged += (_, _) => SelectMappingFromGrid();
        _mappingsGrid.KeyDown += MappingsGridKeyDown;
        _mappingsGrid.MouseDown += MappingsGridMouseDown;
        _mappingsGrid.ContextMenuStrip = BuildMappingsContextMenu();
        _mappingsGrid.AllowDrop = true;
        _mappingsGrid.DragEnter += MappingsGridDragEnter;
        _mappingsGrid.DragDrop += MappingsGridDragDrop;
        ConfigureEmptyState(_mappingsEmptyState, "No mappings configured. Drag a USB device here or double-click a device.");
        mappingsPanel.Controls.Add(_mappingsEmptyState);
        mappingsPanel.Controls.Add(_mappingsGrid);
        mappingsPanel.Controls.Add(header);

        panel.Controls.Add(mappingsPanel, 0, 0);
        panel.Controls.Add(BuildEditorPanel(), 0, 1);
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
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

        panel.Controls.Add(new Label { Text = "Active Service Status", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        panel.Controls.Add(StatusCard("Service", _serviceState), 0, 1);
        panel.Controls.Add(StatusCard("Mappings", _configState), 0, 2);
        panel.Controls.Add(new Label { Text = "Active endpoints", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText }, 0, 3);

        _activeEndpoints.Dock = DockStyle.Fill;
        _activeEndpoints.IntegralHeight = false;
        _activeEndpoints.HorizontalScrollbar = true;
        panel.Controls.Add(_activeEndpoints, 0, 4);

        panel.Controls.Add(new Label { Text = "Warnings", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText }, 0, 5);
        _warnings.Dock = DockStyle.Fill;
        _warnings.IntegralHeight = false;
        _warnings.HorizontalScrollbar = true;
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
        var scroller = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 0, 0),
            ColumnCount = 4,
            RowCount = 8
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        _matchMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _matchMode.Items.AddRange(["usb-location", "usb-serial", "com-name"]);
        _matchMode.SelectedIndex = 0;
        _matchMode.SelectedIndexChanged += (_, _) =>
        {
            UpdateIdentityPreview();
            ValidateEditor();
        };

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

        ConfigureSerialOption(_serialLocal, "local");
        ConfigureSerialOption(_serialNoBreak, "nobreak");
        ConfigureSerialOption(_serialRtsCts, "rtscts");

        _banner.Multiline = true;
        _banner.ScrollBars = ScrollBars.Vertical;
        _banner.Dock = DockStyle.Fill;
        _banner.MinimumSize = new Size(0, 96);

        _identityPreview.Multiline = true;
        _identityPreview.ReadOnly = true;
        _identityPreview.ScrollBars = ScrollBars.Vertical;
        _identityPreview.Dock = DockStyle.Fill;
        _identityPreview.MinimumSize = new Size(0, 96);

        _validation.Dock = DockStyle.Fill;
        _validation.ForeColor = Color.DarkRed;
        _validation.AutoEllipsis = true;

        _addOrUpdate.Text = "Add Mapping";
        _addOrUpdate.AutoSize = true;
        _addOrUpdate.Click += (_, _) => AddOrUpdateMapping();

        _deleteMapping.Text = "Delete Mapping";
        _deleteMapping.AutoSize = true;
        _deleteMapping.Enabled = false;
        _deleteMapping.Click += (_, _) => DeleteSelectedMapping();

        _name.TextChanged += (_, _) => ValidateEditor();
        _listenAddress.TextChanged += (_, _) => ValidateEditor();
        _tcpPort.ValueChanged += (_, _) => ValidateEditor();
        _maxConnections.ValueChanged += (_, _) => ValidateEditor();
        _baud.ValueChanged += (_, _) => ValidateEditor();
        _protocol.SelectedIndexChanged += (_, _) => ValidateEditor();
        _serialSettings.TextChanged += (_, _) => ValidateEditor();
        _enabled.CheckedChanged += (_, _) => ValidateEditor();
        _serialLocal.CheckedChanged += (_, _) => ValidateEditor();
        _serialNoBreak.CheckedChanged += (_, _) => ValidateEditor();
        _serialRtsCts.CheckedChanged += (_, _) => ValidateEditor();

        AddRow(panel, 0, "Alias", _name, "Match by", _matchMode);
        AddRow(panel, 1, "Listen IP", _listenAddress, "Protocol", _protocol);
        AddRow(panel, 2, "TCP Port", _tcpPort, "Baud Rate", _baud);
        AddRow(panel, 3, "Max Clients", _maxConnections, "Serial Format", _serialSettings);
        AddRow(panel, 4, "Serial Options", BuildSerialOptionsPanel(), "", _enabled);
        panel.Controls.Add(new Label { Text = "Banner", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
        panel.Controls.Add(_banner, 1, 5);
        panel.SetColumnSpan(_banner, 3);
        panel.Controls.Add(new Label { Text = "Selected identity", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
        panel.Controls.Add(_identityPreview, 1, 6);
        panel.SetColumnSpan(_identityPreview, 3);
        panel.Controls.Add(new Label { Text = "Validation", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 7);
        panel.Controls.Add(_validation, 1, 7);
        panel.SetColumnSpan(_validation, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 0)
        };
        buttons.Controls.Add(_addOrUpdate);
        buttons.Controls.Add(_deleteMapping);
        buttons.Controls.Add(Button("Clear", ClearEditor));

        scroller.Controls.Add(panel);
        outer.Controls.Add(buttons);
        outer.Controls.Add(scroller);
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

    private static void ConfigureSerialOption(CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.AutoSize = true;
        checkBox.Margin = new Padding(0, 4, 12, 0);
    }

    private Control BuildSerialOptionsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Margin = Padding.Empty
        };
        panel.Controls.Add(_serialLocal);
        panel.Controls.Add(_serialNoBreak);
        panel.Controls.Add(_serialRtsCts);
        return panel;
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

    private ContextMenuStrip BuildMappingsContextMenu()
    {
        var menu = new ContextMenuStrip();
        var delete = menu.Items.Add("Delete Selected Mapping");
        delete.Click += (_, _) => DeleteSelectedMapping();
        menu.Opening += (_, e) =>
        {
            delete.Enabled = HasSelectedMapping();
            e.Cancel = !HasSelectedMapping();
        };
        return menu;
    }

    private static Control StatusCard(string title, Label value)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(0, 0, 0, 6),
            BackColor = SystemColors.Window
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText }, 0, 0);
        value.Dock = DockStyle.Fill;
        value.AutoEllipsis = true;
        panel.Controls.Add(value, 0, 1);
        return panel;
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
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.AllowUserToResizeColumns = true;
        grid.BackgroundColor = SystemColors.Window;
        grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && e.RowIndex < grid.Rows.Count) {
                e.ToolTipText = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            }
        };
    }

    private static void SetColumnWidths(DataGridView grid, params (string Name, int Width)[] widths)
    {
        foreach (var (name, width) in widths) {
            if (grid.Columns.Contains(name)) {
                grid.Columns[name].Width = width;
            }
        }
    }

    private static void ConfigureEmptyState(Label label, string text)
    {
        label.Dock = DockStyle.Fill;
        label.Text = text;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.ForeColor = SystemColors.GrayText;
        label.BackColor = SystemColors.Window;
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

        PopulateDeviceGrid();
        RefreshMappings();
        RefreshStatus();
        ValidateEditor();
    }

    private async Task RefreshDevicesAsync()
    {
        if (_isRefreshing) {
            return;
        }

        _isRefreshing = true;
        SetOperationStatus("Refreshing devices...");
        UpdateActionState();
        try {
            _devices = await Task.Run(() => SerialPortEnumerator.Enumerate().ToList()).ConfigureAwait(true);
            PopulateDeviceGrid();
            RefreshMappings();
            RefreshStatus();
            SetOperationStatus("Ready");
        }
        catch (Exception ex) {
            _devices = [];
            PopulateDeviceGrid();
            SetOperationStatus("Refresh failed");
            AddStatusWarning($"Device scan failed: {ex.Message}");
            MessageBox.Show($"Device scan failed:\n{ex.Message}", "Ser2Net", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally {
            _isRefreshing = false;
            ValidateEditor();
            UpdateActionState();
        }
    }

    private void PopulateDeviceGrid()
    {
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
        _devicesEmptyState.Visible = _devices.Count == 0;
        _devicesGrid.Visible = _devices.Count > 0;
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
                resolvedMapping?.Device.PortName ?? "",
                mapping.Tcp.Port,
                mapping.Tcp.Mode,
                mapping.Enabled ? "Yes" : "No",
                mapping.Name)];
            row.Tag = i;
            row.Cells["Name"].ToolTipText = $"{mapping.Name} | {DeviceNameFromMatch(mapping.Match)} | {mapping.Serial.Baud}{mapping.Serial.Settings}";
            ApplyMappingStatusStyle(row, status);
        }
        _mappingsEmptyState.Visible = _mappingFile.Mappings.Count == 0;
        _mappingsGrid.Visible = _mappingFile.Mappings.Count > 0;
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
            _warnings.Items.Add("No warnings.");
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
            UpdateActionState();
            return;
        }

        _selectedMappingIndex = index;
        _selectedDevice = null;
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
        SetSerialOptions(mapping.Serial.Options);
        _banner.Text = mapping.Banner;
        _addOrUpdate.Text = "Update Mapping";
        UpdateIdentityPreview(mapping.Match);
        ValidateEditor();
        UpdateActionState();
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
        SetSerialOptions([]);
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
            Settings = string.IsNullOrWhiteSpace(_serialSettings.Text) ? "N81" : _serialSettings.Text.Trim().ToUpperInvariant(),
            Options = SelectedSerialOptions()
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

    private string[] SelectedSerialOptions()
    {
        var options = new List<string>();
        if (_serialLocal.Checked) {
            options.Add("local");
        }
        if (_serialNoBreak.Checked) {
            options.Add("nobreak");
        }
        if (_serialRtsCts.Checked) {
            options.Add("rtscts");
        }
        return options.ToArray();
    }

    private void SetSerialOptions(IEnumerable<string> options)
    {
        var normalized = options
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _serialLocal.Checked = normalized.Contains("local");
        _serialNoBreak.Checked = normalized.Contains("nobreak");
        _serialRtsCts.Checked = normalized.Contains("rtscts");
    }

    private void DeleteSelectedMapping()
    {
        if (_selectedMappingIndex < 0 || _selectedMappingIndex >= _mappingFile.Mappings.Count) {
            return;
        }

        var mapping = _mappingFile.Mappings[_selectedMappingIndex];
        var result = MessageBox.Show(
            $"Delete mapping \"{mapping.Name}\"?\n\nTCP {mapping.Tcp.Port} / {mapping.Tcp.Mode}",
            "Delete Mapping",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes) {
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
        SetSerialOptions([]);
        _enabled.Checked = true;
        _banner.Clear();
        _identityPreview.Clear();
        _validation.Text = "";
        _addOrUpdate.Text = "Add Mapping";
        ClearFieldErrors();
        ValidateEditor();
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

    private async Task<bool> SaveAndGenerateAsync(bool showSuccess)
    {
        if (_isSaving) {
            return false;
        }

        if (!ValidateEditor() || !ValidateAllMappings()) {
            MessageBox.Show(_validation.Text, "Ser2Net", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        _isSaving = true;
        SetOperationStatus("Saving configuration...");
        UpdateActionState();
        try {
            var resolved = await Task.Run(() =>
            {
                MappingStore.Save(_paths.MappingFilePath, _mappingFile);
                var latestDevices = SerialPortEnumerator.Enumerate();
                var resolvedMappings = MappingResolver.Resolve(_mappingFile.Mappings, latestDevices);
                Ser2NetConfigGenerator.WriteConfig(_paths.GeneratedConfigPath, resolvedMappings);
                return resolvedMappings.Count;
            }).ConfigureAwait(true);

            RefreshDevices();
            SetOperationStatus("Ready");
            if (showSuccess) {
                MessageBox.Show($"Generated config with {resolved} resolved mapping(s).", "Ser2Net");
            }
            return true;
        }
        catch (Exception ex) {
            SetOperationStatus("Save failed");
            AddStatusWarning($"Save failed: {ex.Message}");
            MessageBox.Show($"Save failed:\n{ex.Message}", "Ser2Net", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally {
            _isSaving = false;
            ValidateEditor();
            UpdateActionState();
        }
    }

    private async Task SaveGenerateAndRestartAsync()
    {
        if (_isRestarting) {
            return;
        }

        if (!await SaveAndGenerateAsync(showSuccess: false).ConfigureAwait(true)) {
            return;
        }

        _isRestarting = true;
        SetOperationStatus("Restarting service...");
        UpdateActionState();
        try {
            await Task.Run(() => RunServiceCommand(_paths, "restart")).ConfigureAwait(true);
            SetOperationStatus("Restart requested");
            RefreshStatus();
        }
        catch (Exception ex) {
            SetOperationStatus("Restart failed");
            AddStatusWarning($"Restart failed: {ex.Message}");
            MessageBox.Show($"Restart failed:\n{ex.Message}", "Ser2Net", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally {
            _isRestarting = false;
            UpdateActionState();
        }
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
        var hasPendingEditorInput = _selectedMappingIndex >= 0 || _selectedDevice is not null || !string.IsNullOrWhiteSpace(_name.Text);
        var alias = _name.Text.Trim();
        ClearFieldErrors();

        if (!hasPendingEditorInput) {
            _validation.Text = "Ready.";
            _validation.ForeColor = Color.DarkGreen;
            _editorIsValid = true;
            UpdateActionState();
            return true;
        }

        if (string.IsNullOrWhiteSpace(alias)) {
            errors.Add("Alias is required.");
            _errors.SetError(_name, "Alias is required.");
        }
        else if (!alias.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')) {
            errors.Add("Alias can only use letters, numbers, dash, underscore, or dot.");
            _errors.SetError(_name, "Use letters, numbers, dash, underscore, or dot.");
        }

        var port = (int)_tcpPort.Value;
        var conflictingMapping = _mappingFile.Mappings
            .Select((mapping, index) => new { mapping, index })
            .FirstOrDefault(item => item.index != _selectedMappingIndex && item.mapping.Enabled && item.mapping.Tcp.Port == port);
        if (conflictingMapping is not null) {
            errors.Add($"TCP port {port} is already used by {conflictingMapping.mapping.Name}.");
            _errors.SetError(_tcpPort, $"Port already used by {conflictingMapping.mapping.Name}.");
        }

        if (_selectedMappingIndex < 0 && _selectedDevice is null) {
            errors.Add("Select or drag a USB/COM device before creating a mapping.");
        }

        if (_selectedDevice is not null) {
            var match = BuildMatch(_selectedDevice);
            if (match.Mode == "usb-location" && string.IsNullOrWhiteSpace(match.LocationPath)) {
                errors.Add("USB location is unavailable; use USB serial or COM-name fallback.");
                _errors.SetError(_matchMode, "USB location is unavailable for this device.");
            }
            if (match.Mode == "usb-serial" && string.IsNullOrWhiteSpace(match.SerialNumber)) {
                errors.Add("USB serial number is unavailable; use USB location or COM-name fallback.");
                _errors.SetError(_matchMode, "USB serial number is unavailable for this device.");
            }
        }

        _validation.Text = errors.Count == 0 ? "Ready." : string.Join(" ", errors);
        _validation.ForeColor = errors.Count == 0 ? Color.DarkGreen : Color.DarkRed;
        _editorIsValid = errors.Count == 0;
        UpdateActionState();
        return _editorIsValid;
    }

    private bool ValidateAllMappings()
    {
        var errors = GetMappingValidationErrors().ToList();
        if (errors.Count == 0) {
            return true;
        }

        _validation.Text = string.Join(" ", errors);
        _validation.ForeColor = Color.DarkRed;
        return false;
    }

    private IEnumerable<string> GetMappingValidationErrors()
    {
        var enabled = _mappingFile.Mappings.Where(m => m.Enabled).ToList();
        foreach (var group in enabled.GroupBy(m => m.Tcp.Port).Where(g => g.Count() > 1)) {
            yield return $"TCP port {group.Key} is used by: {string.Join(", ", group.Select(m => m.Name))}.";
        }

        foreach (var mapping in _mappingFile.Mappings) {
            if (string.IsNullOrWhiteSpace(mapping.Name)) {
                yield return "Every mapping must have an alias.";
            }
            if (mapping.Tcp.Port is < 1 or > 65535) {
                yield return $"{mapping.Name}: TCP port must be between 1 and 65535.";
            }
            if (mapping.Match.Mode == "usb-location" && string.IsNullOrWhiteSpace(mapping.Match.LocationPath)) {
                yield return $"{mapping.Name}: USB Location matching requires a USB location.";
            }
            if (mapping.Match.Mode == "usb-serial" && string.IsNullOrWhiteSpace(mapping.Match.SerialNumber)) {
                yield return $"{mapping.Name}: Serial matching requires a USB serial number.";
            }
        }
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
        var labels = new[]
        {
            "1  Detect Devices",
            "2  Create Mapping",
            "3  Save",
            "4  Restart"
        };
        var completed = new[]
        {
            _devices.Count > 0,
            _mappingFile.Mappings.Count > 0,
            File.Exists(_paths.GeneratedConfigPath),
            GetServiceState().Contains("RUNNING", StringComparison.OrdinalIgnoreCase) && resolvedCount > 0
        };
        var current = Array.IndexOf(completed, false);
        if (current < 0) {
            current = completed.Length - 1;
        }

        for (var i = 0; i < _workflowSteps.Length; i++) {
            if (_workflowSteps[i] is null) {
                continue;
            }

            _workflowSteps[i].Text = completed[i] ? $"{labels[i]}  Done" : labels[i];
            _workflowSteps[i].Font = i == current ? new Font(Font, FontStyle.Bold) : Font;
            _workflowSteps[i].BackColor = completed[i] ? Color.FromArgb(226, 247, 232) : i == current ? Color.FromArgb(230, 245, 255) : SystemColors.Window;
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

    private void MappingsGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Delete || !HasSelectedMapping()) {
            return;
        }

        e.Handled = true;
        DeleteSelectedMapping();
    }

    private void MappingsGridMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) {
            return;
        }

        var hit = _mappingsGrid.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0) {
            return;
        }

        _mappingsGrid.ClearSelection();
        _mappingsGrid.Rows[hit.RowIndex].Selected = true;
        _mappingsGrid.CurrentCell = _mappingsGrid.Rows[hit.RowIndex].Cells[Math.Max(0, hit.ColumnIndex)];
        SelectMappingFromGrid();
    }

    private bool HasSelectedMapping() =>
        _selectedMappingIndex >= 0 && _selectedMappingIndex < _mappingFile.Mappings.Count;

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

    private void SetOperationStatus(string text)
    {
        _operationStatusText.Text = text;
    }

    private void UpdateActionState()
    {
        if (_refreshButton is null || _saveButton is null || _restartButton is null) {
            return;
        }

        var busy = _isRefreshing || _isSaving || _isRestarting;
        _refreshButton.Enabled = !busy;
        _saveButton.Enabled = !busy && _editorIsValid && !GetMappingValidationErrors().Any();
        _restartButton.Enabled = !busy && File.Exists(_paths.GeneratedConfigPath);
        _addOrUpdate.Enabled = !busy && _editorIsValid;
        _deleteMapping.Enabled = !busy && HasSelectedMapping();
        _deleteMappingFromList.Enabled = !busy && HasSelectedMapping();
    }

    private void ClearFieldErrors()
    {
        _errors.SetError(_name, "");
        _errors.SetError(_tcpPort, "");
        _errors.SetError(_matchMode, "");
    }

    private void AddStatusWarning(string warning)
    {
        if (_warnings.Items.Count == 1 && _warnings.Items[0]?.ToString() == "No warnings.") {
            _warnings.Items.Clear();
        }
        _warnings.Items.Add(warning);
    }

    private static class NativeMethods
    {
        public const int SwRestore = 9;

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
