# ser2net Windows Portable Folder 與 Inno Setup 安裝程式執行計劃

計劃日期：2026-06-03
目標平台：Windows 10/11 x64
建置環境：MSYS2 UCRT64 優先，MINGW64 作為備選
交付目標：讓同事不需要安裝 MSYS2/MinGW，也能透過安裝程式部署並執行 ser2net。

## 目標

建立一組已驗證的 Windows portable folder，內含 `ser2net.exe`、所有必要 DLL、預設設定檔與資料目錄，再用 Inno Setup 打包成 `.exe` installer。

第一版不追求完全靜態連結，也不先做 Windows Service。目標是降低部署門檻、提高可重現性，並保留日後擴充成 service installer 的空間。

## 交付物

| 交付物               | 說明                                            |
|----------------------|-------------------------------------------------|
| Portable folder      | 可直接複製到其他 Windows 執行的 `Ser2Net/` 目錄 |
| Inno Setup installer | 給同事安裝用的 `Ser2Net-<version>-win64.exe`    |
| DLL inventory        | 記錄每個隨附 DLL 的來源與用途                   |
| Smoke test 記錄      | 證明 installer 安裝後能啟動與接受 TCP 連線      |
| 更新後的 `.iss`      | 可重複產生 installer 的 Inno Setup 腳本         |

## 建議目錄結構

```text
Ser2Net/
  bin/
    ser2net.exe
    libyaml-0-2.dll
    libgensio*.dll
    libssl*.dll
    libcrypto*.dll
    libwinpthread-1.dll
    其他 ldd 掃出的必要 DLL
  etc/
    ser2net/
      ser2net.yaml
  share/
    ser2net/
  docs/
    README-Windows.zh-TW.md
```

`ser2net.c` 在 Windows 下會使用相對於 executable 的目錄推導 `../etc/ser2net` 與 `../share/ser2net`，因此 installer 應將 `ser2net.exe` 放在 `{app}\bin`，設定檔放在 `{app}\etc\ser2net`。

## 工作階段

### Phase 1：建置環境準備

工作項目：

- 安裝 MSYS2。
- 使用 UCRT64 shell 作為主要 build shell。
- 安裝 autotools、GCC、pkgconf、libyaml。
- 準備 Windows 版 gensio development headers 與 libraries。
- 確認 `gcc`、`autoreconf`、`pkg-config`、`yaml.h`、`gensio/gensio.h` 均可被找到。

建議指令：

```sh
pacman -S --needed base-devel git autoconf automake libtool \
    mingw-w64-ucrt-x86_64-gcc \
    mingw-w64-ucrt-x86_64-pkgconf \
    mingw-w64-ucrt-x86_64-libyaml
```

驗收：

- `gcc --version` 可執行。
- `autoreconf --version` 可執行。
- `pkg-config --modversion yaml-0.1` 可回傳版本。
- gensio headers 與 libraries 已放在固定安裝位置。

### Phase 2：建置 gensio

工作項目：

- 以相同 MSYS2 runtime 建置 gensio。
- 優先產生 shared library，方便與 portable DLL bundle 搭配。
- 記錄 gensio 啟用的功能，例如 SSL、mDNS、IPMI。
- 安裝到固定 staging prefix，例如 `$HOME/install/Gensio`。

驗收：

- `$HOME/install/Gensio/include/gensio/gensio.h` 存在。
- `$HOME/install/Gensio/lib` 內有 gensio import/static libraries。
- `$HOME/install/Gensio/bin` 或 MSYS2 runtime 內有對應 DLL。
- `pkg-config` 或 `CPPFLAGS` / `LDFLAGS` 可讓 ser2net 找到 gensio。

風險：

- gensio 功能開太多會增加 DLL 數量與部署複雜度。
- gensio 若和 ser2net 使用不同 MSYS2 runtime，可能造成連結或執行期問題。

### Phase 3：建置 ser2net

工作項目：

- 執行 `./reconf` 產生 autotools build infrastructure。
- 建立獨立 build 目錄。
- 使用 README 建議的 Windows prefix 設定。
- 編譯並安裝到 staging 目錄。

建議指令：

```sh
./reconf
mkdir -p build-ucrt64
cd build-ucrt64
../configure --sbindir=/Ser2Net/bin --libexecdir=/Ser2Net/bin --mandir=/Ser2Net/man \
    --includedir=/Ser2Net/include --prefix=/Ser2Net \
    CPPFLAGS=-I$HOME/install/Gensio/include LDFLAGS=-L$HOME/install/Gensio/lib
make -j
make install DESTDIR=$HOME/install
```

驗收：

- `$HOME/install/Ser2Net/bin/ser2net.exe` 存在。
- `ser2net.exe` 可顯示版本或使用說明。
- `configure` 沒有啟用 Linux sysfs LED。
- `make` 沒有 unresolved symbol 或 missing DLL 問題。

### Phase 4：建立 portable folder

工作項目：

- 建立乾淨的 `dist/Ser2Net/` staging 目錄。
- 複製 `ser2net.exe` 到 `dist/Ser2Net/bin/`。
- 複製預設 `ser2net.yaml` 到 `dist/Ser2Net/etc/ser2net/`。
- 建立 `dist/Ser2Net/share/ser2net/`。
- 用 `ldd` 掃描 `ser2net.exe` 的 runtime DLL。
- 將必要 DLL 複製到 `dist/Ser2Net/bin/`。
- 建立 DLL inventory 文件。

建議指令：

```sh
ldd dist/Ser2Net/bin/ser2net.exe
```

DLL 納入原則：

- 納入 ser2net 直接依賴的非系統 DLL。
- 納入 gensio 依賴的非系統 DLL。
- 納入 libyaml、OpenSSL、winpthread、GCC runtime 等必要 DLL。
- 不納入 Windows 系統 DLL，例如 `KERNEL32.dll`、`WS2_32.dll`。

驗收：

- 將 `dist/Ser2Net/` 複製到沒有 MSYS2 PATH 的 Windows 環境仍可啟動。
- 在 `dist/Ser2Net/bin` 內執行 `ser2net.exe -h` 或等效命令成功。
- 使用預設或 smoke test 設定可啟動前景程序。

### Phase 5：Smoke test

工作項目：

- 建立最小測試設定檔。
- 在 Windows console 前景執行 ser2net。
- 測試 TCP accepter 可接受 localhost 連線。
- 測試一組實體或虛擬 COM port。
- 若需要 RFC2217，測試基本 serial parameter control。

建議最小設定：

```yaml
%YAML 1.1
---
connection: &com-test
  accepter: tcp,3001
  connector: serialdev,COM3,115200N81
```

建議啟動方式：

```sh
ser2net -n -d -c ..\etc\ser2net\ser2net.yaml
```

驗收：

- ser2net 可在沒有 MSYS2 shell 的 Windows command prompt 啟動。
- `localhost:3001` 可連線。
- 指定 COM port 可開啟。
- 錯誤訊息可定位到設定檔或 DLL 問題。

### Phase 6：更新 Inno Setup 腳本

工作項目：

- 將 `.iss` 的輸入來源改成 portable folder staging 目錄。
- 以 wildcard 或明確清單納入 `bin`、`etc`、`share`、`docs`。
- 設定 installer 輸出檔名含版本與平台。
- 保留 PATH 更新功能，但確認 uninstall 能移除 PATH。
- 新增 Start Menu shortcut。
- 可選：加入安裝完成後開啟 README 或啟動 console 的選項。

建議 `.iss` 概念：

```ini
[Files]
Source: "dist\Ser2Net\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "dist\Ser2Net\etc\*"; DestDir: "{app}\etc"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dist\Ser2Net\share\*"; DestDir: "{app}\share"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dist\Ser2Net\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
```

驗收：

- installer 可在乾淨 Windows VM 安裝。
- 安裝後 `{app}\bin\ser2net.exe` 存在。
- `{app}\etc\ser2net\ser2net.yaml` 存在。
- uninstall 後 PATH 不留下重複或殘留項目。

### Phase 7：乾淨環境驗證

工作項目：

- 準備一台沒有 MSYS2/MinGW PATH 的 Windows 10/11 x64。
- 執行 installer。
- 開啟新的 command prompt。
- 驗證 `ser2net.exe` 可從 PATH 執行，或從 `{app}\bin` 直接執行。
- 執行 smoke test。
- 解除安裝並確認檔案與 PATH 清理。

驗收：

- 不需要安裝 MSYS2。
- 不需要手動複製 DLL。
- 安裝後能啟動 ser2net。
- 移除後不破壞既有 PATH。

## 里程碑

| 里程碑                    | 完成條件                                     |
|---------------------------|----------------------------------------------|
| M1：工具鏈可用             | MSYS2 UCRT64、libyaml、gensio build 環境可用   |
| M2：ser2net 可編譯         | 產生 `ser2net.exe` 並可在 build host 啟動    |
| M3：portable folder 可執行 | `dist/Ser2Net/` 可離開 MSYS2 PATH 執行       |
| M4：installer 可安裝       | Inno Setup 產出 installer 並可安裝到 `{app}` |
| M5：乾淨機器驗證通過       | 無 MSYS2 的 Windows 上通過 smoke test        |

## 風險與對策

| 風險                                 | 影響                          | 對策                                         |
|--------------------------------------|-------------------------------|----------------------------------------------|
| DLL 漏包                             | 同事機器無法啟動              | 以 `ldd` 建立 DLL inventory，並在乾淨 VM 驗證 |
| gensio 功能依賴過多                  | installer 變大、DLL 清單變複雜 | 第一版只保留必要功能，後續再加 SSL/mDNS/IPMI  |
| COM port 行為和 Linux serialdev 不同 | 實際連接設備失敗              | 使用實體或虛擬 COM port 做 smoke test        |
| PATH 修改造成污染                    | 安裝/解除安裝體驗差           | PATH 加入前檢查重複，uninstall 後驗證         |
| Windows Service 過早導入             | 權限、帳號與維護複雜度增加     | 第一版只做 console app，service 放到第二階段  |
| 完全靜態連結不可行                   | 延誤交付                      | 明確排除在第一版之外，採 portable DLL bundle  |

## 不納入第一版的工作

- 完全靜態連結單一 exe。
- Windows Service 自動安裝與啟動。
- GUI 設定工具。
- 自動偵測 COM port 並產生設定檔。
- 程式碼層級 Windows port 重構。
- upstream Linux test suite 全量移植。

## 第一版成功定義

第一版完成時，同事應能取得一個 installer，在沒有 MSYS2/MinGW 的 Windows 10/11 x64 上安裝後，直接啟動 ser2net，使用預設或指定的 `ser2net.yaml` 開啟 TCP accepter，並連接到指定 COM port。

若達到這個狀態，即可先內部散佈測試；後續再根據使用情境決定是否加入 Windows Service、簽章、版本化 release pipeline 或完全靜態連結嘗試。
