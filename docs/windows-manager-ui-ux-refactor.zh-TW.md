# Windows Ser2Net Manager UI/UX 重構提案

日期：2026-06-04
目標 app：Windows Ser2Net Manager
目標使用者：需要把 USB serial console 映射成 RFC2217/Telnet service port 的 embedded engineers 與 automation agents
實作目標：WinUI 3 desktop app，沿用現有 `Ser2Net.Windows.Core` 的 discovery、mapping、resolver 與 config generation layer

## 1. UX 分析

目前 manager 已經能完成核心技術流程：掃描 USB/COM devices、建立 stable mapping、產生 `windows.yml`，並重啟 service。主要問題是 setup、maintenance、service control、file navigation 等操作被放在接近相同的優先層級。新使用者必須自行推論正確操作順序。

重構後應該把產品定位成 operational console：

- 主畫面是三欄 workspace，不是一組鬆散按鈕。
- 預設路徑清楚可見：detect device、create/edit mapping、save、restart。
- Service 與 mapping health 要和可編輯設定分開呈現。
- Mapping editor 要有足夠寬度與高度支援實際欄位驗證。
- Advanced operations 移到 Tools menu，不和主要流程競爭。
- Automation 先保留成 extension surface，但第一版不干擾手動 workflow。

主要 persona：

| Persona | 需求 | UI 影響 |
|---------|------|---------|
| Embedded engineer | 快速把已知 USB console 映射到固定 TCP port | 優先呈現 device list、mapping table、drag-to-create、清楚 service state |
| Lab operator | 在交付連線前確認哪些 console 正在運作 | 使用 badges、status summary、resolved COM/TCP visibility |
| AI agent / automation | 產生可重複 config 並偵測衝突 | 保持可命令化 state model，並保留 Automation panel |
| New user | 理解必要操作順序 | 加入 workflow stepper，並依 prerequisite 啟用/停用 action |

目前痛點與設計回應：

| 痛點 | 重構回應 |
|------|----------|
| 太多操作被放在同一層級 | Main toolbar 只保留 Refresh Devices、Save、Restart Service |
| Mapping workflow 不明顯 | 加入四步驟 workflow strip 與 contextual empty states |
| 重要狀態難辨識 | 加入 status badges 與右側 service status panel |
| Configuration panel 太小 | 把 mapping editor 提升成中間主面板 |
| 新使用者不知道操作順序 | Workflow strip 與 primary buttons 直接反映必要順序 |

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

窄版 layout 行為：

- 寬度低於 1000 px 時，右側 status panel 收合成 Status tab。
- 寬度低於 760 px 時，device panel、mapping editor、status panel 變成 top-level tabs。
- Bottom status bar 在視窗開啟時永遠可見。

## 3. WinUI 3 實作提案

Manager window 建議使用 WinUI 3，tray process 仍作為 user-session host。若需要降低遷移風險，tray icon 初期可以繼續使用 WinForms/NotifyIcon，而 main manager 開啟 WinUI 3 window。關鍵架構變更是把 UI state 移到 view models，不再嵌在 form event handlers 裡。

建議 projects：

```text
windows/
  Ser2Net.Windows.Core/          # 現有 discovery、mapping、resolver、generator
  Ser2Net.Service/               # 現有 service wrapper
  Ser2Net.Manager/               # 新 WinUI 3 desktop manager
  Ser2Net.Tray/                  # tray host，可開啟 Manager
```

建議實作 stack：

| 區域 | 建議 |
|------|------|
| UI framework | WinUI 3 / Windows App SDK |
| Pattern | MVVM with observable view models |
| State | 單一 `ManagerWorkspaceViewModel`，再拆出各 panel state |
| Commands | `ICommand` / relay commands for Refresh, Save, Restart, Generate |
| Validation | `MappingEditorViewModel` 實作 `INotifyDataErrorInfo` |
| Drag-and-drop | `ListViewBase.CanDragItems` 與 mapping list 的 `Drop` handler |
| Status badges | `InfoBadge`、colored `Border` 或自訂 `StatusBadge` control |
| Tools menu | 依最終 shell 使用 `MenuBar` 或 `AppBarButton` flyout |
| Accessibility | Named controls、keyboard order、AutomationProperties、high-contrast resources |

主要流程：

1. `RefreshDevicesCommand`
   - 呼叫 `SerialPortEnumerator.Enumerate()`。
   - 更新 `DetectedDevices`。
   - 重新 resolve mappings。
   - 更新 workflow step 1。

2. `QuickCreateMappingCommand(device)`
   - 建立新的 `PortMapping`。
   - Alias 使用 `${com-port-lower}-console`。
   - TCP port 從 3001 開始找下一個可用 port。
   - Baud 預設 115200，protocol 依 project default 使用 RFC2217 或 Telnet。
   - 在 editor 選取新 mapping。

3. `SaveCommand`
   - 驗證 editor fields。
   - 寫入 `mappings.json`。
   - 產生 `windows.yml`。
   - 更新 mapping status 為 Configured 或 Error。

4. `RestartServiceCommand`
   - 透過現有 service control path 執行 service restart。
   - 重新整理 service state。
   - 更新 active endpoint status。

Validation rules：

| Field | Rule | Severity |
|-------|------|----------|
| Alias | Required、unique、YAML anchor safe | Error |
| TCP Port | 1-65535、enabled mappings 之間唯一、可偵測時不得被其他 process 佔用 | Error |
| Baud Rate | 支援的 integer range，預設 115200 | Error |
| Protocol | `rfc2217`、`telnet` 或支援的 `ser2net` accepter mode | Error |
| Max Clients | 1-1000 | Error |
| Match rule | 除非明確使用 COM-only fallback，否則需要 USB serial/location | Warning 或 Error |
| Enabled | Disabled mapping 不阻擋 port uniqueness，除非設定為保留 port | Info |

Status model：

```csharp
public enum MappingStatus
{
    Running,
    ConfiguredNotActive,
    Error
}
```

Badge 意義：

| Badge | 意義 |
|-------|------|
| Green `Running` | Mapping 已 enabled、已 resolve 到 COM port、已產生到 config，且 service restart 後觀測為 active |
| Yellow `Configured` | Mapping 已儲存，但 service 尚未套用、device 缺席，或需要 restart |
| Red `Error` | 因 validation、unresolved device、service failure 或 TCP conflict 導致無法產生或啟動 |

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

View model hierarchy：

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

Top area 分三層：

1. Title bar：`Ser2Net Manager`。
2. Primary toolbar：`Refresh Devices`、`Save`、`Restart Service`、`Tools`。
3. Workflow stepper：四步驟 sequence。

Primary toolbar 行為：

| Button | Enabled when | Result |
|--------|--------------|--------|
| Refresh Devices | 永遠可用 | 更新 device list 與 mapping resolution |
| Save | Mapping state dirty 且 valid | 儲存 `mappings.json` 並產生 `windows.yml` |
| Restart Service | Saved config 存在且 service 已安裝 | 重啟 service 並重新整理 status |

Tools menu：

- Generate Config
- Open Config Folder
- Open Logs
- Install Service
- Uninstall Service
- Export Diagnostics

### Left Panel: Detected USB Devices

目的：回答「目前有哪些 device 可以拿來 map？」

每個 device row/card 顯示：

- Device name
- COM port
- Serial number，或 `No serial number`
- VID/PID
- USB location summary
- Current mapping state：unmapped、mapped、conflict 或 missing metadata

互動：

- 選取 device 後 preview identity information。
- Double-click quick-create mapping。
- Drag device 到 mapping list 以 quick-create mapping。
- Context menu：Create Mapping、Copy Device ID、Copy Location Path。

### Center Panel: Mapping Configuration Editor

目的：回答「這個 device 應該變成哪個 service port？」

Center panel 垂直分成：

- 上半部：mapping list with compact status rows。
- 下半部：selected mapping form。

Mapping row fields：

- Status badge
- Device Name
- COM Port
- Serial Number
- TCP Port
- Protocol
- Enabled State

Editor fields：

- Alias
- TCP Port
- Baud Rate
- Protocol
- Max Clients
- Banner
- Enabled

Validation 顯示在 field 下方，並彙總影響 Save button 狀態。範例：

```text
TCP port 3001 is already used by rack-1-console.
```

Quick-create defaults：

| Field | Default |
|-------|---------|
| Alias | `com4-console` |
| TCP Port | 從 `3001` 起算的下一個 free port |
| Baud Rate | `115200` |
| Protocol | 可用時使用 `rfc2217`，否則 `telnet` |
| Max Clients | `10` |
| Enabled | `true` |

### Right Panel: Active Service Status

目的：回答「實際正在運作的是什麼？」

Sections：

- Service state：Running、Stopped、Not installed、Restarting、Error。
- Config freshness：Saved、Unsaved changes、Restart pending、Generate failed。
- Active endpoints：TCP port、alias、protocol、resolved COM。
- Warnings：missing device、duplicate TCP port、service restart failed。
- Automation：保留 collapsed/empty panel 給未來 AI-assisted workflows。

Automation panel 應該是一個真正的 component 與 placeholder view model，而不是 code comment。這樣未來加入 AI generated mapping、conflict detection、MCP registration、MCP export 時，不需要再次重做 shell layout。

### Bottom Status Bar

固定格式：

```text
Service: Running | Mappings: 3 | USB Devices: 4 | Config: C:\ProgramData\Ser2Net\etc\ser2net\windows.yml
```

規則：

- 永遠可見。
- 空間不足時 config path 從中間 truncate。
- Tooltip 顯示完整 config path。
- Status bar context menu 提供 Copy Config Path。

## 6. Accessibility Improvements

Keyboard：

- `Alt+R`：Refresh Devices。
- `Ctrl+S`：Save。
- `Ctrl+Shift+R`：Restart Service。
- `F6`：在 left、center、right、status areas 之間切換。
- `Enter`：從 selected device quick-create，或編輯 selected mapping。
- `Delete`：確認後刪除 selected mapping。

Screen reader：

- 每個 icon button、badge、list、field 都要設定 `AutomationProperties.Name` 與 `HelpText`。
- Status badges 必須 expose `Running`、`Configured but not active`、`Error` 等文字，不可只靠顏色。
- Validation messages 要和對應 input field 關聯。

Visual：

- Status 使用 color 加文字或 icon，不只使用顏色。
- 透過 theme resources 支援 Windows high-contrast mode。
- 最小 touch target 維持 32 x 32 px。
- 不截斷 COM port、TCP port 與 status labels。
- 在 125%、150%、200% display scaling 都要保持可讀 layout。

Focus and errors：

- Save 失敗時，把 focus 移到第一個 invalid field。
- Validation summary 變更要透過 accessible live region 宣告。
- Clear All Mappings 這類 destructive actions 放在 Tools menu 或確認對話框後，不放在 primary workflow。

## Existing Codebase 實作注意事項

現有 `Ser2Net.Windows.Core` 是好的重構基礎。WinUI 3 manager 應沿用：

- `SerialPortEnumerator`
- `MappingStore`
- `MappingResolver`
- `Ser2NetConfigGenerator`
- `Ser2NetPaths`
- 現有 service CLI 的 `start`、`stop`、`restart`、`generate`

目前 WinForms `ManagerForm` 可在 migration 期間作為 fallback。第一個 WinUI milestone 應先用新 layout 重現現有行為，再加入更深的 service telemetry。

建議 migration phases：

| Phase | Scope | Acceptance |
|-------|-------|------------|
| UI-1 | 加入 WinUI shell、toolbar、workflow strip、三欄 layout，先用 mock view models | App 可開啟且 resize 正常 |
| UI-2 | 綁定真實 device discovery 與 mapping load/save | Refresh、quick-create、edit、save 可運作 |
| UI-3 | 加入 validation、badges、status bar、Tools menu | Save 前可看見 duplicate TCP port 與 unresolved device |
| UI-4 | 接上 service state 與 restart flow | Running/configured/error states 對應真實 service 狀態 |
| UI-5 | 加入 drag-and-drop quick-create 與 Automation placeholder | Device 拖到 mapping list 可建立 default mapping |

