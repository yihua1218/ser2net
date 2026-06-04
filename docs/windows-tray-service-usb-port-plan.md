# Windows Tray Service and Stable USB Serial Mapping Plan

Plan date: 2026-06-04
Target platform: Windows 10/11 x64
Scope: a Windows notification-area configuration tool, a service runner for `ser2net.exe`, and stable mapping from physical USB serial adapters to fixed TCP ports.

## Executive Recommendation

Build a small Windows management layer around the existing packaged `ser2net.exe` instead of changing `ser2net` itself first.

The recommended design is:

1. A Windows Service runs `ser2net.exe` in the background and owns restart/reload behavior.
2. A user-session tray app edits mappings, shows status, and controls the service through a local IPC API.
3. A port resolver maps USB serial adapters to user-defined endpoint names and fixed TCP ports.
4. The resolver discovers the current `COMx` name at runtime and generates the active `ser2net.yaml`.

This means the product should not depend on Windows always assigning the same `COMx` number. It should offer fixed network ports, stable labels, and optional COM-number pinning only as an advanced administrative feature.

## Problem Statement

The current Windows sample configuration uses a direct `serialdev,COM4,115200N81` connector. That is simple, but it assumes the serial adapter always appears as `COM4`.

For USB serial adapters this is not always safe:

- Windows COM numbers may change when a device is plugged into a different USB path, when a similar adapter is installed, or when a driver is replaced.
- Some USB serial adapters expose a unique hardware serial number; others do not.
- A fixed COM number solves only the local Windows name. The real user goal is usually fixed external access, such as "the adapter in this physical USB position is always TCP 3001".

The management tool should therefore treat the COM number as an implementation detail and expose stable mappings at a higher level.

## Windows Research Notes

Microsoft recommends discovering and accessing serial ports through `GUID_DEVINTERFACE_COMPORT` instead of relying only on legacy COM-port names, because legacy names can collide and do not provide state-change notifications to clients:
https://learn.microsoft.com/en-us/windows-hardware/drivers/install/guid-devinterface-comport

Windows apps and services can receive plug-and-play device notifications. `RegisterDeviceNotification` supports window and service recipients, and services can receive `SERVICE_CONTROL_DEVICEEVENT` through their control handler:
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerdevicenotificationa

The Windows Forms `NotifyIcon` component is a standard way to put a process in the Windows notification area and attach a menu or management UI:
https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon

Windows services are installed into the Service Control Manager database with APIs such as `CreateService`, or through installer tooling that wraps the same service model:
https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-createservicea

## Architecture

### Components

| Component              | Responsibility                                                                 |
|------------------------|---------------------------------------------------------------------------------|
| `ser2net.exe`          | Existing TCP-to-serial bridge process                                           |
| Ser2Net Windows Service | Starts, stops, monitors, and restarts `ser2net.exe` with generated config       |
| Tray app               | User-facing status, configuration editor, service control, and notifications     |
| Port resolver          | Enumerates serial ports and resolves configured match rules to current `COMx`    |
| Config generator       | Writes active `ser2net.yaml` from stable mappings                               |
| Local state store      | Stores user mappings, resolved device identities, and generated config metadata  |

### Runtime Layout

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
    windows-tray-service-usb-port-plan.md
```

The generated `windows.yml` remains a normal `ser2net` YAML file. That keeps the core program unchanged and lets advanced users inspect or run the generated configuration manually.

## Stable USB Serial Mapping Strategy

### Preferred Rule: Device Serial Number

When a USB serial adapter exposes a real USB serial number, bind by:

- USB VID
- USB PID
- USB serial number
- Optional interface number, such as `MI_00`

This is the most portable stable identity. It survives unplug/replug and usually survives moving to another USB port.

### Fallback Rule: Physical USB Location

When the adapter does not expose a unique serial number, bind by physical location:

- USB hub/controller location path
- USB port chain
- Interface number
- Driver-reported hardware ID

This supports the user's "fixed position" requirement: the adapter plugged into the same hub port maps to the same network port. It will intentionally follow the USB socket, not the adapter.

### Last-Resort Rule: Current COM Name

Allow `COMx` matching for quick setup and debugging only. The UI should mark this as less stable.

### Stable External Contract

The user's stable configuration should be:

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

The generated `ser2net` entry then uses the COM name currently resolved by Windows:

```yaml
connection: &rack-1-console
  accepter: telnet,tcp,0.0.0.0,3001
  connector: serialdev,COM4,115200N81
```

If the same physical USB position later resolves to `COM7`, the service regenerates:

```yaml
connection: &rack-1-console
  accepter: telnet,tcp,0.0.0.0,3001
  connector: serialdev,COM7,115200N81
```

The network client still connects to TCP `3001`.

## COM Number Pinning

COM-number pinning should be supported only as an optional admin tool, not as the default model.

Reasoning:

- It requires elevated permissions.
- It must handle collisions with existing COM names.
- It depends on driver behavior and registry-backed device properties.
- It does not solve the higher-level requirement as well as stable TCP-port mapping.

Recommended UI behavior:

- Default action: "Bind this device/location to TCP port".
- Advanced action: "Try to reserve current device as COMx".
- Before pinning, scan current COM usage and warn on conflicts.
- If pinning fails, keep the stable TCP-port mapping functional.

The implementation can use Windows serial-port enumeration and COM database APIs to inspect usage, but the product should not promise universal COM renumbering across all third-party drivers.

## Tray App Design

### Primary Views

| View       | Purpose                                                                  |
|------------|--------------------------------------------------------------------------|
| Status     | Service state, active mappings, online/offline devices, bound TCP ports  |
| Devices    | Detected serial ports with COM name, VID/PID, serial number, location    |
| Mappings   | Stable aliases, match rule, TCP port, baud/settings, enabled state       |
| Logs       | Service logs, ser2net stdout/stderr, last config-generation result       |
| Settings   | install/uninstall service, auto-start, log path, config path             |

### Tray Menu

- Open Ser2Net Manager
- Start Service
- Stop Service
- Restart Service
- Reload Mapping
- Open Config Folder
- Open Logs
- Exit Tray App

The tray app should be per-user and does not run `ser2net` directly. It talks to the service through named pipes or localhost-only HTTP. Operations that require admin rights, such as service installation or COM pinning, should trigger elevation only for that action.

## Service Design

The service should:

- Load `mappings.json`.
- Enumerate serial devices through SetupAPI / Configuration Manager using `GUID_DEVINTERFACE_COMPORT`.
- Resolve every enabled mapping to a current COM port.
- Generate `etc/ser2net/windows.yml`.
- Start `ser2net.exe -n -c <generated config>`.
- Capture stdout/stderr to logs.
- Subscribe to device arrival/removal notifications.
- Debounce hotplug events and regenerate/restart when mappings change.

Because `ser2net` already has a working Windows executable package, the first service version should treat `ser2net.exe` as a child process. A future version can investigate embedding service support into `ser2net`, but that increases upstream patch surface and is not needed for a first usable tool.

## Handling Hotplug

Recommended behavior:

1. Device arrival/removal event fires.
2. Service waits a short debounce interval, such as 1 to 3 seconds.
3. Service re-enumerates serial ports.
4. If the resolved mapping set changed, regenerate YAML.
5. Restart `ser2net.exe` with the new config.
6. Notify the tray app of mapping status changes.

For a missing device, choose one of two configurable behaviors:

- Omit that connection from generated YAML so remaining ports keep working.
- Keep a disabled placeholder in the UI and show "device missing".

The first release should omit missing mappings from YAML and show the error in the tray UI.

## Implementation Phases

### Phase 1: Discovery Prototype

Work items:

- Build a small Windows command-line tool that lists COM ports.
- For each port, print COM name, device instance ID, hardware IDs, VID/PID, serial number if available, location path, manufacturer, and friendly name.
- Test with at least two USB serial adapters and one no-serial-number adapter if available.

Acceptance:

- The tool can distinguish two identical adapters when they expose serial numbers.
- The tool can distinguish two fixed USB hub positions when adapters do not expose serial numbers.
- The output includes enough data to create `mappings.json`.

### Phase 2: Config Generator

Work items:

- Define `mappings.json`.
- Generate `windows.yml` with one `connection` per resolved mapping.
- Validate generated YAML by starting `ser2net.exe` in foreground.
- Confirm whether gensio accepts `COM10+` names directly or needs `\\.\COM10` syntax in the connector string.

Acceptance:

- `rack-1-console -> TCP 3001 -> current COMx` works.
- Missing devices do not break unrelated mappings.
- Generated YAML is readable and manually debuggable.

### Phase 3: Service Runner

Work items:

- Implement a Windows Service wrapper.
- Start and monitor `ser2net.exe`.
- Add logging and child-process restart policy.
- Add hotplug-triggered regeneration/restart.

Acceptance:

- Service starts at boot.
- Service can recover from `ser2net.exe` exit.
- Plugging/removing a configured USB serial adapter updates active mappings.

### Phase 4: Tray Manager

Work items:

- Implement a .NET Windows tray app with `NotifyIcon`.
- Add status, devices, mappings, logs, and settings views.
- Add service control actions.
- Add mapping wizard: select detected device, choose identity rule, choose TCP port and serial settings.

Acceptance:

- A normal user can see status and logs.
- A normal user can edit mappings if the config directory ACL allows it.
- Admin-only actions request elevation explicitly.

### Phase 5: Installer Integration

Work items:

- Extend the existing Inno Setup package.
- Install service and tray binaries.
- Add optional "Install Windows Service" checkbox.
- Add Start Menu shortcut for tray manager.
- Configure service auto-start only when selected.

Acceptance:

- Clean Windows 10/11 VM install works without MSYS2.
- Service starts from Services UI or tray app.
- Uninstall stops/removes service and leaves user mapping files only if the user chooses to keep them.

## Risks and Mitigations

| Risk                                      | Impact                                      | Mitigation                                                             |
|-------------------------------------------|---------------------------------------------|-------------------------------------------------------------------------|
| USB adapter has no unique serial number   | Device identity follows port location only  | Offer USB-location binding and explain that moving sockets changes role  |
| COM number changes after reboot/hotplug   | Direct `COMx` config breaks                 | Generate YAML from stable mapping at service start and hotplug events    |
| COM pinning collides with existing device | Device may become inaccessible              | Make pinning advanced/admin-only and pre-check current usage             |
| `ser2net` restart interrupts sessions     | Active network clients disconnect           | Debounce events and restart only when resolved mapping actually changes  |
| Tray app cannot elevate silently          | Some operations need admin confirmation      | Keep everyday mapping/status actions non-admin where possible            |
| `COM10+` syntax differs by gensio behavior| High-numbered COM ports fail                | Add explicit Phase 2 validation before relying on high COM numbers       |

## Decision Summary

The best first solution is stable TCP-port mapping by USB identity or USB physical location, implemented by a Windows Service and tray manager that generate normal `ser2net` YAML. Fixed COM numbering can be offered as a secondary convenience, but it should not be the primary reliability mechanism.

This approach keeps the existing `ser2net.exe` and packaging work useful, gives users a Windows-native management experience, and directly satisfies the requirement that a device connected at a fixed physical USB position can always appear at a fixed network port.

## Initial Implementation Status

The first implementation has been added under `windows/`:

- `Ser2Net.Windows.Core`: serial-device discovery through SetupAPI, mapping models, mapping resolution, and generated `ser2net` YAML output.
- `Ser2Net.Service`: Windows service entrypoint plus CLI commands for `list-devices`, `generate`, `run-console`, `install`, `uninstall`, `start`, `stop`, and `restart`.
- `Ser2Net.Tray`: Windows Forms notification-area app with service actions, device/status display, mapping generation, and shortcuts to mapping/config files.
- The Manager UI now supports the common setup flow directly: scan USB/COM console lines, select one device, create a fixed service-port mapping from that USB physical location, edit alias/listen address/protocol/TCP port/max clients/baud/serial settings/banner text, save mappings, generate YAML, and restart the service. New mappings default to `max-connections: 10`.

The GitHub Actions Windows package workflow now installs .NET 8, publishes `Ser2Net.Service.exe` and `Ser2Net.Tray.exe` into `dist/Ser2Net/bin`, runs config generation, validates the new files, and packages them into both the Inno Setup installer and portable zip.

For installed Windows systems, mutable configuration and logs live under `C:\ProgramData\Ser2Net` instead of `C:\Program Files\Ser2Net`. The installer creates `C:\ProgramData\Ser2Net\etc\ser2net` and `C:\ProgramData\Ser2Net\logs` with Users modify permissions, while binaries remain under Program Files. Portable and CI runs that pass `--root` still keep config inside the portable root.

Remaining follow-up work:

- Add full hotplug notification and debounce inside the service.
- Improve the mapping wizard with richer validation and conflict warnings.
- Add optional admin-only COM-number pinning.
- Add integration tests with real USB serial adapters, including a `COM10+` validation case.
