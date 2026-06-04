# Windows Ser2Net Manager UI/UX Refactor Proposal

Date: 2026-06-04
Target app: Windows Ser2Net Manager
Target users: embedded engineers and automation agents mapping USB serial consoles to RFC2217/Telnet service ports
Implementation target: WinUI 3 desktop app, backed by the existing `Ser2Net.Windows.Core` discovery, mapping, resolver, and config-generation layer

## 1. UX Analysis

The current manager already solves the core technical workflow: scan USB/COM devices, create a stable mapping, generate `windows.yml`, and restart the service. The usability problem is that the UI exposes setup, maintenance, service control, and file-navigation actions at nearly the same priority. New users have to infer the correct sequence.

The refactor should make the product behave like an operational console:

- The primary screen is a three-panel workspace, not a loose collection of buttons.
- The default path is visible: detect device, create or edit mapping, save, restart.
- Service and mapping health are visually distinct from editable configuration.
- The mapping editor has enough width and height to support real field validation.
- Advanced operations move out of the top toolbar into a Tools menu.
- Automation is represented as a reserved extension surface, but does not compete with the manual workflow in the first release.

Primary personas:

| Persona | Need | UI implication |
|---------|------|----------------|
| Embedded engineer | Quickly map a known USB console to a fixed TCP port | Prioritize device list, mapping table, drag-to-create, and clear service state |
| Lab operator | See which consoles are live before handing access to others | Use badges, status summary, and resolved COM/TCP visibility |
| AI agent / automation | Generate repeatable config and detect conflicts | Keep a commandable state model and reserve an Automation panel |
| New user | Understand the required operation order | Add workflow stepper and disable actions until prerequisites are met |

Current pain points and design responses:

| Pain point | Refactor response |
|------------|-------------------|
| Too many equal-priority actions | Keep only Refresh Devices, Save, and Restart Service in the main toolbar |
| Mapping workflow is unclear | Add a four-step workflow strip and contextual empty states |
| Important status is hard to see | Add status badges and a right-side service status panel |
| Configuration panel is too small | Promote the mapping editor to the center panel |
| New users do not know the sequence | Make the workflow strip and primary buttons reflect the required sequence |

## 2. Wireframe Mockup

```text
┌────────────────────────────────────────────────────────────────────────────────────────────┐
│ Ser2Net Manager                                      [Refresh Devices] [Save] [Restart] Tools│
├────────────────────────────────────────────────────────────────────────────────────────────┤
│ 1 Detect USB Devices  →  2 Create/Edit Mapping  →  3 Save Configuration  →  4 Restart Service │
├───────────────────────┬──────────────────────────────────────────┬─────────────────────────┤
│ Detected USB Devices  │ Mapping Configuration Editor              │ Active Service Status   │
│                       │                                          │                         │
│ Search/filter         │ Mappings                                 │ Service: ● Running      │
│ ┌───────────────────┐ │ ┌──────────────────────────────────────┐ │ Config: up to date      │
│ │ COM4  USB Serial  │ │ │ ● rack-1-console  COM4 → TCP 3001   │ │ ser2net.exe: running    │
│ │ FTDI UART         │ │ │   RFC2217  115200  Enabled          │ │ Last restart: 10:42     │
│ │ S/N A50285BI      │ │ ├──────────────────────────────────────┤ │                         │
│ │ VID/PID 0403/6001 │ │ │ ● com7-console    COM7 → TCP 3002   │ │ Active endpoints        │
│ └───────────────────┘ │ │   Telnet   115200  Enabled          │ │ 3001 rack-1-console    │
│                       │ └──────────────────────────────────────┘ │ 3002 com7-console      │
│ ┌───────────────────┐ │                                          │                         │
│ │ COM7  USB Serial  │ │ Selected Mapping                         │ Warnings                │
│ │ no serial number  │ │ Alias        [rack-1-console          ]  │ none                    │
│ │ USB location bind │ │ TCP Port     [3001]  Protocol [RFC2217]  │                         │
│ └───────────────────┘ │ Baud Rate    [115200] Max Clients [10]  │ Automation              │
│                       │ Banner       [                       ]  │ Reserved for AI mapping │
│ Drag device here  ───▶│ Enabled      [x]                         │ and MCP export          │
│                       │ Validation: port 3001 is available       │                         │
├───────────────────────┴──────────────────────────────────────────┴─────────────────────────┤
│ Service: Running | Mappings: 3 | USB Devices: 4 | Config: C:\ProgramData\Ser2Net\...\windows.yml │
└────────────────────────────────────────────────────────────────────────────────────────────┘
```

Narrow layout behavior:

- Under 1000 px width, the right status panel collapses into a Status tab.
- Under 760 px width, the device panel, mapping editor, and status panel become top-level tabs.
- The bottom status bar remains visible whenever the window is open.

## 3. WinUI 3 Implementation Proposal

Use WinUI 3 for the manager window and keep the tray process as the user-session host. The tray icon can remain WinForms/NotifyIcon initially if needed, while the main manager opens a WinUI 3 window. The key architectural change is to move UI state into view models instead of embedding it in form event handlers.

Recommended projects:

```text
windows/
  Ser2Net.Windows.Core/          # existing discovery, mapping, resolver, generator
  Ser2Net.Service/               # existing service wrapper
  Ser2Net.Manager/               # new WinUI 3 desktop manager
  Ser2Net.Tray/                  # tray host; can open Manager
```

Recommended implementation stack:

| Area | Recommendation |
|------|----------------|
| UI framework | WinUI 3 / Windows App SDK |
| Pattern | MVVM with observable view models |
| State | Single `ManagerWorkspaceViewModel` with derived panels |
| Commands | `ICommand` / relay commands for Refresh, Save, Restart, Generate |
| Validation | `INotifyDataErrorInfo` on `MappingEditorViewModel` |
| Drag-and-drop | `ListViewBase.CanDragItems`, `Drop` handler into mapping list |
| Status badges | `InfoBadge`, colored `Border`, or custom `StatusBadge` control |
| Tools menu | `MenuBar` or `AppBarButton` flyout depending on final shell |
| Accessibility | Named controls, keyboard order, AutomationProperties, high-contrast resources |

Main flows:

1. `RefreshDevicesCommand`
   - Calls `SerialPortEnumerator.Enumerate()`.
   - Updates `DetectedDevices`.
   - Re-resolves mappings.
   - Updates workflow step 1.

2. `QuickCreateMappingCommand(device)`
   - Creates a new `PortMapping`.
   - Uses alias `${com-port-lower}-console`.
   - Uses the next available TCP port starting at 3001.
   - Defaults baud to 115200, protocol to RFC2217 or Telnet based on project default.
   - Selects the new mapping in the editor.

3. `SaveCommand`
   - Validates editor fields.
   - Writes `mappings.json`.
   - Generates `windows.yml`.
   - Updates mapping status to Configured or Error.

4. `RestartServiceCommand`
   - Runs the service restart command through the existing service control path.
   - Refreshes service state.
   - Updates active endpoint status.

Validation rules:

| Field | Rule | Severity |
|-------|------|----------|
| Alias | Required, unique, YAML anchor safe | Error |
| TCP Port | 1-65535, unique among enabled mappings, not known active by another process when detectable | Error |
| Baud Rate | Supported integer range, default 115200 | Error |
| Protocol | `rfc2217`, `telnet`, or supported `ser2net` accepter mode | Error |
| Max Clients | 1-1000 | Error |
| Match rule | USB serial/location required unless COM-only fallback is explicit | Warning or Error |
| Enabled | Disabled mappings do not block port uniqueness unless configured to reserve ports | Info |

Status model:

```csharp
public enum MappingStatus
{
    Running,
    ConfiguredNotActive,
    Error
}
```

Badge meanings:

| Badge | Meaning |
|-------|---------|
| Green `Running` | Mapping is enabled, resolved to a COM port, generated into config, and observed active after service restart |
| Yellow `Configured` | Mapping is saved but service has not picked it up, device is absent, or restart is pending |
| Red `Error` | Mapping cannot be generated or activated due to validation, unresolved device, service failure, or TCP conflict |

## 4. Component Hierarchy

```text
Ser2NetManagerApp
└─ MainWindow
   ├─ AppTitleBar
   │  ├─ WindowTitle
   │  ├─ PrimaryToolbar
   │  │  ├─ RefreshDevicesButton
   │  │  ├─ SaveButton
   │  │  └─ RestartServiceButton
   │  └─ ToolsMenu
   │     ├─ GenerateConfigMenuItem
   │     ├─ OpenConfigFolderMenuItem
   │     └─ OpenLogsMenuItem
   ├─ WorkflowStepper
   │  ├─ DetectUsbDevicesStep
   │  ├─ CreateEditMappingStep
   │  ├─ SaveConfigurationStep
   │  └─ RestartServiceStep
   ├─ WorkspaceGrid
   │  ├─ DetectedDevicesPanel
   │  │  ├─ DeviceSearchBox
   │  │  ├─ DeviceList
   │  │  │  └─ DeviceCard
   │  │  └─ EmptyDeviceState
   │  ├─ MappingEditorPanel
   │  │  ├─ MappingList
   │  │  │  └─ MappingRow
   │  │  ├─ MappingDropTarget
   │  │  └─ MappingForm
   │  │     ├─ AliasField
   │  │     ├─ TcpPortField
   │  │     ├─ BaudRateField
   │  │     ├─ ProtocolSelector
   │  │     ├─ MaxClientsField
   │  │     ├─ BannerField
   │  │     ├─ EnabledToggle
   │  │     └─ ValidationSummary
   │  └─ ServiceStatusPanel
   │     ├─ ServiceStateCard
   │     ├─ ActiveEndpointList
   │     ├─ WarningList
   │     └─ AutomationPanelPlaceholder
   └─ BottomStatusBar
      ├─ ServiceStatusText
      ├─ MappingCountText
      ├─ UsbDeviceCountText
      └─ ConfigPathText
```

View model hierarchy:

```text
ManagerWorkspaceViewModel
├─ ObservableCollection<SerialDeviceViewModel> DetectedDevices
├─ ObservableCollection<PortMappingViewModel> Mappings
├─ MappingEditorViewModel SelectedMapping
├─ ServiceStatusViewModel ServiceStatus
├─ AutomationPanelViewModel Automation
├─ WorkflowStateViewModel Workflow
└─ Commands
   ├─ RefreshDevicesCommand
   ├─ QuickCreateMappingCommand
   ├─ SaveCommand
   ├─ RestartServiceCommand
   ├─ GenerateConfigCommand
   ├─ OpenConfigFolderCommand
   └─ OpenLogsCommand
```

## 5. Refactored Screen Layout

### Top Area

The top area has three layers:

1. Title bar: `Ser2Net Manager`.
2. Primary toolbar: `Refresh Devices`, `Save`, `Restart Service`, and `Tools`.
3. Workflow stepper: the four-step sequence.

Primary toolbar behavior:

| Button | Enabled when | Result |
|--------|--------------|--------|
| Refresh Devices | Always | Updates device list and mapping resolution |
| Save | Mapping state is dirty and valid | Saves `mappings.json` and generates `windows.yml` |
| Restart Service | Saved config exists and service is installed | Restarts service and refreshes status |

Tools menu:

- Generate Config
- Open Config Folder
- Open Logs
- Install Service
- Uninstall Service
- Export Diagnostics

### Left Panel: Detected USB Devices

Purpose: answer "what can I map?"

Each device row/card shows:

- Device name
- COM port
- Serial number, or `No serial number`
- VID/PID
- USB location summary
- Current mapping state: unmapped, mapped, conflict, or missing metadata

Interactions:

- Select a device to preview identity information.
- Double-click to quick-create a mapping.
- Drag a device into the mapping list to quick-create a mapping.
- Context menu: Create Mapping, Copy Device ID, Copy Location Path.

### Center Panel: Mapping Configuration Editor

Purpose: answer "what service port should this device become?"

The center panel is split vertically:

- Upper section: mapping list with compact status rows.
- Lower section: selected mapping form.

Mapping row fields:

- Status badge
- Device Name
- COM Port
- Serial Number
- TCP Port
- Protocol
- Enabled State

Editor fields:

- Alias
- TCP Port
- Baud Rate
- Protocol
- Max Clients
- Banner
- Enabled

Validation appears inline below the field and summarized above the Save button state. Example:

```text
TCP port 3001 is already used by rack-1-console.
```

Quick-create defaults:

| Field | Default |
|-------|---------|
| Alias | `com4-console` |
| TCP Port | Next free port from `3001` |
| Baud Rate | `115200` |
| Protocol | `rfc2217` if available, otherwise `telnet` |
| Max Clients | `10` |
| Enabled | `true` |

### Right Panel: Active Service Status

Purpose: answer "what is actually running?"

Sections:

- Service state: Running, Stopped, Not installed, Restarting, Error.
- Config freshness: Saved, Unsaved changes, Restart pending, Generate failed.
- Active endpoints: TCP port, alias, protocol, resolved COM.
- Warnings: missing device, duplicate TCP port, service restart failed.
- Automation: reserved collapsed/empty panel for future AI-assisted workflows.

The Automation panel should be a first-class component with a placeholder view model, not a comment in the code. This keeps future features such as AI-generated mapping, conflict detection, MCP registration, and MCP export from forcing another shell redesign.

### Bottom Status Bar

Fixed format:

```text
Service: Running | Mappings: 3 | USB Devices: 4 | Config: C:\ProgramData\Ser2Net\etc\ser2net\windows.yml
```

Rules:

- Always visible.
- Truncate the config path in the middle when space is limited.
- Tooltip shows the full config path.
- Copy Config Path is available from the status bar context menu.

## 6. Accessibility Improvements

Keyboard:

- `Alt+R`: Refresh Devices.
- `Ctrl+S`: Save.
- `Ctrl+Shift+R`: Restart Service.
- `F6`: cycle left, center, right, status areas.
- `Enter`: quick-create from selected device or edit selected mapping.
- `Delete`: delete selected mapping after confirmation.

Screen reader:

- Add `AutomationProperties.Name` and `HelpText` for every icon button, badge, list, and field.
- Status badges must expose text such as `Running`, `Configured but not active`, and `Error`; do not rely on color alone.
- Validation messages should be associated with the corresponding input field.

Visual:

- Use color plus text or icon for status, never color alone.
- Support Windows high-contrast mode through theme resources.
- Keep minimum touch target size at 32 x 32 px.
- Avoid truncating COM port, TCP port, and status labels.
- Preserve readable layout at 125%, 150%, and 200% display scaling.

Focus and errors:

- When Save fails, move focus to the first invalid field.
- Announce validation summary changes through an accessible live region.
- Keep destructive actions such as Clear All Mappings in the Tools menu or behind confirmation, not in the primary workflow.

## Implementation Notes for the Existing Codebase

The existing `Ser2Net.Windows.Core` layer is a good base for the refactor. The WinUI 3 manager should reuse:

- `SerialPortEnumerator`
- `MappingStore`
- `MappingResolver`
- `Ser2NetConfigGenerator`
- `Ser2NetPaths`
- the existing service CLI for `start`, `stop`, `restart`, and `generate`

The current WinForms `ManagerForm` can remain as a fallback during migration. The first WinUI milestone should reproduce the current behavior with the new layout before adding deeper service telemetry.

Recommended migration phases:

| Phase | Scope | Acceptance |
|-------|-------|------------|
| UI-1 | Add WinUI shell, toolbar, workflow strip, three-panel layout using mock view models | App opens and resizes correctly |
| UI-2 | Bind real device discovery and mapping load/save | Refresh, quick-create, edit, save work |
| UI-3 | Add validation, badges, status bar, and Tools menu | Duplicate TCP port and unresolved device are visible before save |
| UI-4 | Wire service state and restart flow | Running/configured/error states match service reality |
| UI-5 | Add drag-and-drop quick-create and Automation placeholder | Device-to-mapping drag creates default mapping |

