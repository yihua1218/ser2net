# Windows 狀態列服務工具與 USB Serial 穩定映射計劃

計劃日期：2026-06-04
目標平台：Windows 10/11 x64
範圍：Windows notification area / 狀態列設定工具、`ser2net.exe` 服務啟動器，以及把固定 USB serial adapter 映射到固定 TCP port 的機制。

## 建議結論

建議先在既有 Windows 版 `ser2net.exe` 外面加一層 Windows 管理工具，不要第一步就改 `ser2net` 核心程式。

合理的產品設計是：

1. Windows Service 負責在背景啟動、停止、監控與重啟 `ser2net.exe`。
2. 使用者登入後執行 tray app，負責設定、狀態顯示與服務控制。
3. port resolver 把 USB serial adapter 對應到使用者定義的 alias 與固定 TCP port。
4. resolver 在 runtime 解析目前 Windows 給的 `COMx`，再產生實際給 `ser2net` 使用的 `ser2net.yaml`。

也就是說，產品不應該依賴 Windows 永遠給同一個 `COMx` 編號。使用者真正需要的是固定的 network endpoint，例如「插在這個固定 USB 位置的 console 永遠是 TCP 3001」。固定 COM 編號可以做成進階管理功能，但不應該是主要可靠性機制。

## 問題定義

目前 Windows sample config 直接使用 `serialdev,COM4,115200N81`。這個設定很直覺，但前提是 serial adapter 永遠都是 `COM4`。

對 USB serial adapter 來說，這個假設不夠穩：

- Windows 可能因為換 USB 位置、插入相似 adapter、更新 driver，而改變 COM 編號。
- 有些 USB serial adapter 有唯一硬體序號，有些沒有。
- 固定 COM 編號只解決本機 Windows 名稱；使用者真正要的是固定外部連線，例如固定 USB 位置永遠映射到固定 network port。

所以管理工具應該把 COM 編號視為底層實作細節，對外提供更穩定的 alias 與 TCP port。

## Windows 研究摘要

Microsoft 建議用 `GUID_DEVINTERFACE_COMPORT` 來探索與存取 serial port，而不是只依賴傳統 COM port 名稱，因為傳統名稱可能衝突，也不能提供狀態變更通知：
https://learn.microsoft.com/en-us/windows-hardware/drivers/install/guid-devinterface-comport

Windows app 與 service 可以接收 plug-and-play device notification。`RegisterDeviceNotification` 支援 window 與 service recipient，service 也可以透過 control handler 收到 `SERVICE_CONTROL_DEVICEEVENT`：
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerdevicenotificationa

Windows Forms 的 `NotifyIcon` 是把程式放在 Windows notification area 並提供選單/管理 UI 的標準元件：
https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon

Windows service 會安裝到 Service Control Manager database，可透過 `CreateService` 這類 API 或 installer tooling 建立：
https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-createservicea

## 系統架構

### 元件

| 元件                    | 職責                                                                |
|-------------------------|---------------------------------------------------------------------|
| `ser2net.exe`           | 既有 TCP-to-serial bridge 程式                                      |
| Ser2Net Windows Service | 啟動、停止、監控、重啟 `ser2net.exe`，並使用產生後的 config             |
| Tray app                | 使用者面向的狀態、設定編輯、服務控制與通知                            |
| Port resolver           | 列舉 serial ports，並把設定裡的 match rule 解析成目前的 `COMx`       |
| Config generator        | 從穩定 mapping 產生 active `ser2net.yaml`                           |
| Local state store       | 儲存使用者 mapping、解析後的 device identity、產生 config 的 metadata |

### Runtime 目錄配置

```text
Ser2Net/
  bin/
    ser2net.exe
    Ser2Net.Service.exe
    Ser2Net.Tray.exe
  etc/
    ser2net/
      windows.yml              # generated active config
      mappings.json            # stable source-of-truth mapping file
      mappings.schema.json
  logs/
    service.log
    ser2net.log
  docs/
    windows-tray-service-usb-port-plan.zh-TW.md
```

產生後的 `windows.yml` 仍然是一般 `ser2net` YAML。這樣可以保持核心程式不變，也讓進階使用者能直接檢查或手動執行該 config。

## 穩定 USB Serial 映射策略

### 優先規則：Device Serial Number

如果 USB serial adapter 有真正的 USB serial number，優先用下列欄位綁定：

- USB VID
- USB PID
- USB serial number
- optional interface number，例如 `MI_00`

這是最穩定的 device identity。它通常可以承受拔掉重插，也通常可以承受換到別的 USB port。

### 備援規則：Physical USB Location

如果 adapter 沒有唯一 serial number，就改用 physical location 綁定：

- USB hub/controller location path
- USB port chain
- interface number
- driver 回報的 hardware ID

這正好符合「固定位置」的需求：插在同一個 hub port 的 adapter 會映射到同一個 network port。這個規則會跟著 USB 插座，而不是跟著 adapter 本體。

### 最後手段：Current COM Name

允許用 `COMx` 做快速設定與 debug，但 UI 要標示這個方式比較不穩。

### 對外穩定契約

使用者真正穩定保存的設定應該長這樣：

```json
{
  "name": "rack-1-console",
  "match": {
    "mode": "usb-location",
    "locationPath": "PCIROOT(0)#PCI(... )#USBROOT(0)#USB(4)",
    "vid": "0403",
    "pid": "6001",
    "interface": "MI_00"
  },
  "tcp": {
    "mode": "telnet",
    "listenAddress": "0.0.0.0",
    "port": 3001
  },
  "serial": {
    "baud": 115200,
    "settings": "N81"
  }
}
```

工具產生給 `ser2net` 的實際 entry 則使用目前 Windows 解析到的 COM 名稱：

```yaml
connection: &rack-1-console
  accepter: telnet,tcp,0.0.0.0,3001
  connector: serialdev,COM4,115200N81
```

如果同一個 USB 位置後來變成 `COM7`，service 會重新產生：

```yaml
connection: &rack-1-console
  accepter: telnet,tcp,0.0.0.0,3001
  connector: serialdev,COM7,115200N81
```

network client 仍然固定連到 TCP `3001`。

## 固定 COM 編號

固定 COM 編號應該做成 optional admin tool，不建議作為預設模式。

原因：

- 需要 elevated permission。
- 必須處理既有 COM 名稱衝突。
- 行為會受到 driver 與 registry-backed device property 影響。
- 它沒有 stable TCP-port mapping 那麼直接滿足使用者需求。

建議 UI 行為：

- 預設操作：「把這個 device/location 綁到 TCP port」。
- 進階操作：「嘗試把目前 device 保留為 COMx」。
- pinning 前先掃描目前 COM 使用狀況，衝突時提示風險。
- pinning 失敗時，stable TCP-port mapping 仍然要能正常工作。

實作上可以使用 Windows serial-port enumeration 與 COM database API 檢查使用狀態，但產品不應承諾所有第三方 driver 都能被可靠重新編號。

## Tray App 設計

### 主要畫面

| 畫面     | 用途                                                                 |
|----------|----------------------------------------------------------------------|
| Status   | service state、active mappings、online/offline devices、bound TCP ports |
| Devices  | 偵測到的 serial ports，包含 COM name、VID/PID、serial number、location   |
| Mappings | stable aliases、match rule、TCP port、baud/settings、enabled state       |
| Logs     | service logs、ser2net stdout/stderr、最後一次 config generation 結果   |
| Settings | install/uninstall service、auto-start、log path、config path            |

### Tray Menu

- Open Ser2Net Manager
- Start Service
- Stop Service
- Restart Service
- Reload Mapping
- Open Config Folder
- Open Logs
- Exit Tray App

tray app 應該是 per-user process，不直接執行 `ser2net`。它透過 named pipe 或 localhost-only HTTP 和 service 溝通。需要 admin 權限的操作，例如 service installation 或 COM pinning，只在該操作發生時要求 elevation。

## Service 設計

service 應該負責：

- 讀取 `mappings.json`。
- 透過 SetupAPI / Configuration Manager 與 `GUID_DEVINTERFACE_COMPORT` 列舉 serial devices。
- 把每個 enabled mapping 解析成目前 COM port。
- 產生 `etc/ser2net/windows.yml`。
- 啟動 `ser2net.exe -n -c <generated config>`。
- 將 stdout/stderr 寫入 logs。
- 訂閱 device arrival/removal notifications。
- hotplug event debounce 後，在 mapping 有變化時重新產生 config 並重啟。

因為目前已經有可用的 Windows `ser2net.exe` package，第一版 service 應該把 `ser2net.exe` 視為 child process。未來可以研究把 service support 直接嵌進 `ser2net`，但那會增加 upstream patch surface，不是第一版必要條件。

## Hotplug 處理

建議流程：

1. 收到 device arrival/removal event。
2. service 等待短暫 debounce，例如 1 到 3 秒。
3. service 重新列舉 serial ports。
4. 如果 resolved mapping set 有變化，就重新產生 YAML。
5. 用新 config 重啟 `ser2net.exe`。
6. 通知 tray app 更新 mapping status。

device 缺席時可以有兩種設定：

- 從 generated YAML 省略該 connection，讓其他 port 繼續運作。
- UI 保留 disabled placeholder，顯示 "device missing"。

第一版建議從 YAML 省略 missing mapping，並在 tray UI 顯示錯誤。

## 實作階段

### Phase 1: Discovery Prototype

工作項目：

- 建立一個 Windows command-line tool 列出 COM ports。
- 對每個 port 印出 COM name、device instance ID、hardware IDs、VID/PID、serial number、location path、manufacturer、friendly name。
- 至少用兩個 USB serial adapters 測試；若可取得，也測一個沒有 serial number 的 adapter。

驗收：

- adapter 有 serial number 時，工具能分辨兩個相同型號 adapter。
- adapter 沒有 serial number 時，工具能分辨兩個固定 USB hub 位置。
- 輸出資料足以建立 `mappings.json`。

### Phase 2: Config Generator

工作項目：

- 定義 `mappings.json`。
- 為每個 resolved mapping 產生一個 `windows.yml` connection。
- 用 foreground `ser2net.exe` 驗證 generated YAML。
- 確認 gensio 對 `COM10+` 是可直接接受 `COM10`，還是 connector string 必須使用 `\\.\COM10`。

驗收：

- `rack-1-console -> TCP 3001 -> current COMx` 可運作。
- missing device 不會讓其他 mapping 壞掉。
- generated YAML 可讀、可手動 debug。

### Phase 3: Service Runner

工作項目：

- 實作 Windows Service wrapper。
- 啟動並監控 `ser2net.exe`。
- 加入 logging 與 child-process restart policy。
- 加入 hotplug-triggered regeneration/restart。

驗收：

- service 可在 boot 後自動啟動。
- service 可在 `ser2net.exe` exit 後復原。
- 插入/移除已設定的 USB serial adapter 時，active mapping 會更新。

### Phase 4: Tray Manager

工作項目：

- 用 .NET Windows tray app 與 `NotifyIcon` 實作 UI。
- 加入 status、devices、mappings、logs、settings views。
- 加入 service control actions。
- 加入 mapping wizard：選擇偵測到的 device、選擇 identity rule、選擇 TCP port 與 serial settings。

驗收：

- 一般使用者可以看 status 與 logs。
- 如果 config directory ACL 允許，一般使用者可以編輯 mappings。
- admin-only actions 會明確要求 elevation。

### Phase 5: Installer Integration

工作項目：

- 擴充目前 Inno Setup package。
- 安裝 service 與 tray binaries。
- 加入 optional "Install Windows Service" checkbox。
- 加入 tray manager 的 Start Menu shortcut。
- 只有使用者選擇時才設定 service auto-start。

驗收：

- clean Windows 10/11 VM 不需要 MSYS2 即可安裝。
- service 可從 Services UI 或 tray app 啟動。
- uninstall 會停止/移除 service；使用者 mapping files 則依使用者選項保留或移除。

## 風險與對策

| 風險                                 | 影響                              | 對策                                                                |
|--------------------------------------|-----------------------------------|---------------------------------------------------------------------|
| USB adapter 沒有唯一 serial number   | device identity 只能跟著 USB 位置 | 提供 USB-location binding，並清楚說明換插座會改變角色                |
| reboot/hotplug 後 COM 編號改變       | 直接 `COMx` config 失效           | service start 與 hotplug event 時都從 stable mapping 重新產生 YAML  |
| COM pinning 和既有 device 衝突       | device 可能無法使用               | pinning 設為 advanced/admin-only，並在執行前掃描目前使用狀況         |
| `ser2net` restart 會中斷既有 session | network client 斷線               | hotplug event debounce，且只有 resolved mapping 真的變更時才 restart |
| tray app 無法無聲 elevation          | 部分操作需要 admin confirmation   | 日常 mapping/status 操作盡量不需要 admin                            |
| `COM10+` syntax 受 gensio 行為影響   | 高編號 COM port 開啟失敗          | Phase 2 必須明確驗證高編號 COM port syntax                          |

## 決策摘要

第一版最合適的解法，是用 Windows Service 與 tray manager 從 USB identity 或 USB physical location 產生一般 `ser2net` YAML，讓使用者得到穩定的 TCP-port mapping。固定 COM 編號可以作為次要便利功能，但不應該成為主要可靠性機制。

這個做法能保留目前既有 `ser2net.exe` 與 installer 成果，提供 Windows-native 管理體驗，也直接滿足「接在固定 USB 位置的設備，要固定映射到固定 network port」這個需求。

## 初版實作狀態

第一版實作已加入 `windows/`：

- `Ser2Net.Windows.Core`：透過 SetupAPI 做 serial-device discovery、mapping models、mapping resolution，以及產生 `ser2net` YAML。
- `Ser2Net.Service`：Windows service entrypoint，並提供 `list-devices`、`generate`、`run-console`、`install`、`uninstall`、`start`、`stop`、`restart` CLI commands。
- `Ser2Net.Tray`：Windows Forms notification-area app，提供 service actions、device/status display、mapping generation，以及開啟 mapping/config files 的捷徑。
- Manager UI 現在已支援常用設定流程：掃描 USB/COM console 線、選取其中一條、用 USB physical location 建立固定 service-port mapping、編輯 alias/listen address/protocol/TCP port/max clients/baud/serial settings/banner text、儲存 mappings、產生 YAML，並可重啟 service。新的 mapping 預設會產生 `max-connections: 10`。

GitHub Actions Windows package workflow 現在會安裝 .NET 8，將 `Ser2Net.Service.exe` 與 `Ser2Net.Tray.exe` publish 到 `dist/Ser2Net/bin`，執行 config generation，驗證新增檔案，並把它們一起包進 Inno Setup installer 與 portable zip。

在正式安裝到 Windows 時，可變設定與 logs 放在 `C:\ProgramData\Ser2Net`，不放在 `C:\Program Files\Ser2Net`。installer 會建立 `C:\ProgramData\Ser2Net\etc\ser2net` 與 `C:\ProgramData\Ser2Net\logs`，並給 Users modify 權限；程式 binaries 則維持在 Program Files。portable 與 CI 若使用 `--root`，設定仍會留在 portable root 內。

後續仍需補上的工作：

- service 內完整的 hotplug notification 與 debounce。
- mapping wizard 的驗證與衝突提示，例如 TCP port 重複、USB location 缺失、COM-only fallback。
- optional admin-only COM-number pinning。
- 使用真實 USB serial adapters 的 integration tests，包含 `COM10+` 驗證案例。
