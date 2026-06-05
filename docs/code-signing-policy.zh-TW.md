# Code Signing Policy

本文件說明 Ser2Net Windows release artifacts 的 build、review、approval 與 code signing 規則。

Free code signing provided by [SignPath.io](https://about.signpath.io/),
certificate by [SignPath Foundation](https://signpath.org/)。

## 適用範圍

本 policy 適用於 `.github/workflows/windows-package.yml` 產生的 Windows release artifacts：

- `Ser2Net-<version>-win64.exe`
- `Ser2Net-<version>-win64-runtime.exe`
- `Ser2Net-<version>-win64-portable.zip`
- `Ser2Net-<version>-win64-runtime-portable.zip`

installer 會包含 `ser2net.exe`、Windows service wrapper、Windows tray manager、必要的 open-source runtime libraries、預設設定檔與文件。

## 團隊角色

Committers 與 reviewers 是對 source repository 有 write access 的 maintainers。沒有 commit access 的人送出的 pull request，必須經過 review 才能 merge。

Approvers 是被授權核准 SignPath signing request 的 maintainers。approver 只能在確認 artifact 來自 repository 自動化 release workflow，且對應正確 source revision 或 tag 後，才可核准 signing request。

所有負責 commit、review 或 signing approval 的 team members，都必須對 repository access 與 SignPath access 啟用 multi-factor authentication。

## Build Provenance

Windows release artifacts 由 GitHub Actions 從 source build。workflow 會 checkout repository、編譯 MSYS2/UCRT64 `ser2net.exe`、publish .NET Windows service 與 tray tools、驗證 package 必要內容、建立 Inno Setup installers、建立 portable zip files，並在 version tag release 時上傳 release assets。

不應核准任何不是由文件化 release workflow 產生的 artifacts。

## 可簽章項目

只有從此 repository build 出來的 Ser2Net project binaries 與 installers 可以依此 policy 簽章。

installer 或 portable zip 可以包含執行 Windows package 所需的 unsigned open-source upstream binaries 與 runtime libraries，但除非它們是在此 repository 中 build 且由本專案維護，否則不應把它們當成 Ser2Net project binaries 簽章。

## Privacy

請見 [Privacy Policy](privacy.zh-TW.md)。

除非使用者或安裝/操作此軟體的人明確設定，Ser2Net 不會把資訊傳送到其他 networked systems。Ser2Net 的用途是把使用者設定的 local serial、console 或 gensio endpoints bridge 到使用者設定的 network endpoints。

## 系統變更

Windows installer 可能會：

- 將檔案安裝到 `C:\Program Files\Ser2Net`
- 在 `C:\ProgramData\Ser2Net` 建立可寫入的設定與 log 目錄
- 選擇性安裝並啟動 `Ser2Net` Windows service
- 選擇性讓 Ser2Net tray manager 在 Windows 啟動時自動執行
- 將已安裝的 `bin` 目錄加入 system `PATH`

installer 也提供 uninstall actions，用來停止並移除 Windows service，以及從 system `PATH` 移除已安裝的 `bin` 目錄。

## Release Approval Checklist

核准 SignPath signing request 前，approver 應確認：

- signing request 對應預期的 release tag 或已核准的 workflow run。
- source revision 符合預期 release。
- Windows package workflow 已成功完成。
- 必要 artifacts 均存在。
- binary product names 與 product versions 設定一致。
- release notes 或 download page 已描述 Windows package 的功能。
- signed package 沒有加入 proprietary components。
