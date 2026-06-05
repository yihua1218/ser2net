# SignPath Foundation 申請準備資料

本文件整理申請免費 SignPath.io subscription 與 SignPath Foundation certificate 前需要準備的資料。

## 申請摘要

Project name: Ser2Net

Project type: open-source Windows installer 與 portable package。Ser2Net 是 configurable bridge，用來連接 gensio accepters 與 gensio connectors，常見用途包含把 local serial ports 或 IPMI Serial-over-LAN connections 透過設定好的 network endpoints 對外提供。

Repository: `https://github.com/cminyard/ser2net`

主要 Windows artifacts：

- `Ser2Net-<version>-win64.exe`
- `Ser2Net-<version>-win64-runtime.exe`
- `Ser2Net-<version>-win64-portable.zip`
- `Ser2Net-<version>-win64-runtime-portable.zip`

Build workflow: `.github/workflows/windows-package.yml`

Code signing policy: `docs/code-signing-policy.md`

Privacy policy: `docs/privacy.md`

## Eligibility Checklist

- OSS license：確認所有 signed project code 都使用 repository 的 open-source license，bundled upstream libraries 是 open source 或 system libraries。
- Maintained project：確認 repository 有 active maintainers 與近期 release activity。
- Released project：先發布至少一版 unsigned Windows package，格式要和之後要簽章的版本相同。
- Documented download page：GitHub Release page 需要描述 Windows installer、portable packages、system changes 與 uninstall behavior。
- Code signing policy：從 project home page 與 Windows release/download page 連到 `docs/code-signing-policy.md`。
- Privacy policy：從 code signing policy 與 release/download page 連到 `docs/privacy.md`。
- MFA：committers、reviewers、approvers 的 repository access 與 SignPath access 都要啟用 multi-factor authentication。
- Metadata：確認 signed binaries 與 installers 使用 `Ser2Net` product name，且同一個 build 使用同一個 release version。

## Release Page Text

Windows artifacts 的 GitHub Release page 可使用以下文字：

```markdown
## Windows Packages

This release includes Windows x64 packages for Ser2Net:

- `Ser2Net-<version>-win64.exe`: framework-dependent installer. Requires the
  .NET Desktop Runtime 8 on the target machine.
- `Ser2Net-<version>-win64-runtime.exe`: self-contained installer with the
  required .NET runtime bundled.
- `Ser2Net-<version>-win64-portable.zip`: framework-dependent portable folder.
- `Ser2Net-<version>-win64-runtime-portable.zip`: self-contained portable
  folder with the required .NET runtime bundled.

The installer may install files under `C:\Program Files\Ser2Net`, create
configuration and log directories under `C:\ProgramData\Ser2Net`, optionally
install and start the `Ser2Net` Windows service, optionally start the tray
manager when Windows starts, and add the installed `bin` directory to the
system `PATH`. The uninstaller stops and removes the service and removes the
installed `bin` directory from the system `PATH`.

Code signing policy: `docs/code-signing-policy.md`
Privacy policy: `docs/privacy.md`
```

SignPath 通過後，再加上：

```markdown
Free code signing provided by SignPath.io, certificate by SignPath Foundation.
```

## 建議申請表回答

Project description:

```text
Ser2Net is an open-source tool that bridges configurable gensio accepters and
connectors. It is commonly used to expose local serial ports or IPMI
Serial-over-LAN connections through configured network endpoints. The Windows
package adds a Windows service wrapper, tray manager, default configuration,
and Inno Setup installer around the open-source ser2net binary.
```

What should be signed:

```text
Windows release artifacts built by GitHub Actions from this repository:
Ser2Net-<version>-win64.exe, Ser2Net-<version>-win64-runtime.exe, and project
binaries inside the package such as ser2net.exe, Ser2Net.Service.exe, and
Ser2Net.Tray.exe. Portable zip files are published as release assets; contained
project binaries should be signed before packaging.
```

How artifacts are built:

```text
The repository uses .github/workflows/windows-package.yml on GitHub Actions.
The workflow builds ser2net.exe under MSYS2/UCRT64, publishes the .NET 8
Windows service and tray tools, validates the package layout, builds Inno Setup
installers, creates portable zip files, uploads artifacts, and publishes release
assets when a version tag is pushed.
```

Privacy statement:

```text
Ser2Net does not transfer information to networked systems unless specifically
requested by the user or the person installing or operating it. The software
only opens or connects endpoints configured by the operator. It does not
include telemetry, analytics, advertising, or automatic crash report upload.
```

System changes:

```text
The Windows installer may install files under C:\Program Files\Ser2Net, create
configuration and log directories under C:\ProgramData\Ser2Net, optionally
install and start the Ser2Net Windows service, optionally start the tray manager
when Windows starts, and add the installed bin directory to the system PATH.
The uninstaller stops and removes the Windows service and removes the installed
bin directory from the system PATH.
```

## 通過後整合 TODO

- 建立 SignPath project 與 artifact configuration。
- 將 SignPath API credentials 加到 GitHub Actions secrets。
- 在 installer 與 zip package 前，先簽 `ser2net.exe`、`Ser2Net.Service.exe` 與 `Ser2Net.Tray.exe`。
- 對最終 Inno Setup installers 加 signing step。
- 使用 `signtool verify /pa /v` 驗證 signed files。
- unsigned upstream DLLs 可以被包含在 package 裡，但除非它們是在此 repository 中 build 且由本專案維護，否則不要當成 Ser2Net project binaries 簽章。
