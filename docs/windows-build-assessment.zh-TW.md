# ser2net Windows 編譯與執行可行性評估

評估日期：2026-06-03
評估範圍：目前工作目錄 `d:\Workspace\ser2net` 的原始碼、建置設定、文件與本機工具鏈狀態。

## 結論

此專案可以編譯成在 Windows 上執行的版本，但目標環境不是 Visual Studio/MSVC 原生建置，而是 MSYS2 的 UCRT64 或 MINGW64 工具鏈。專案 README 已明確列出 Windows Support，指出需要 Windows 版 gensio、`mingw-w64-x86_64-libyaml`，並建議在 UCRT64/MINGW64 下建置，最後可用 `ser2net.iss` 產生 Inno Setup 安裝程式。

後續已在本機補齊 MSYS2 UCRT64、autotools、GCC、libyaml、OpenSSL、gensio 與 Inno Setup Compiler，並完成實際編譯與 portable installer 打包驗證。可行性已從靜態評估提升為本機 build 通過。

## 支援狀態摘要

| 項目               | 評估                                                                               |
|--------------------|------------------------------------------------------------------------------------|
| Windows 編譯可行性 | 可行，但需要 MSYS2 UCRT64/MINGW64                                                   |
| MSVC 原生編譯      | 不建議，專案未提供 CMake、Visual Studio solution 或 MSVC 相容建置流程                |
| Windows 執行形態   | 一般 Windows console executable，搭配相對於 exe 的 `etc/ser2net` 與 `share/ser2net` |
| 安裝包             | 已提供 Inno Setup 腳本 `ser2net.iss`                                               |
| 測試完整性         | upstream 測試主要限 Linux，Windows 需要另設 smoke/integration test                  |
| 主要風險           | gensio Windows build、DLL 打包完整性、serialdev/COM port 行為驗證                    |

## 專案內的 Windows 支援證據

1. README 的 Windows Support 段落明確表示可以為 Windows 建置，並指定 UCRT64/MINGW64 與 `mingw-w64-x86_64-libyaml`。
   - 來源：`README.rst:315-321`

2. README 說明 Windows 不使用 Unix 的 `sysconfdir` / `datarootdir`，改用相對於 executable 的 `../etc/ser2net` 與 `../share/ser2net`。
   - 來源：`README.rst:323-325`

3. README 提供 Windows 安裝路徑設定範例，並說明可透過 `ser2net.iss` 產生 executable installer。
   - 來源：`README.rst:327-338`

4. `ser2net.c` 內有 `_WIN32` 條件編譯，使用 `GetModuleFileNameA()` 取得執行檔位置，用來推導 Windows 預設資料目錄。
   - 來源：`ser2net.c:88-116`

5. `ser2net.c` 將 syslog、pidfile cleanup、daemon detach/fork/setsid 等 POSIX 行為包在 `#ifndef WIN32` 內，避免 Windows build 直接碰到這些 Unix-only API。
   - 來源：`ser2net.c:260-287`, `ser2net.c:527-577`

6. `ser2net.iss` 已存在，會打包 `ser2net.exe` 與 `libyaml-0-2.dll` 到 `{app}/bin`，並加入 PATH。
   - 來源：`ser2net.iss:8-9`, `ser2net.iss:36-38`

## 建置系統與相依性

專案使用 autotools：

- `reconf` 會依序執行 `libtoolize`、`aclocal`、`autoconf`、`automake -a`。
- `configure.ac` 檢查 `gensio/gensio.h`、`libgensio`、`libgensioosh`、`libgensiomdns`、`yaml.h` 與 `libyaml`。
- `Makefile.am` 將主要原始碼編成單一 `ser2net` 程式，並包含 `tests` 子目錄。

必備前置條件：

- MSYS2 UCRT64 或 MINGW64 shell
- `base-devel`
- `autoconf`
- `automake`
- `libtool`
- UCRT64/MINGW64 的 `gcc`
- UCRT64/MINGW64 的 `libyaml`
- Windows 版 gensio development headers 與 libraries
- 若要打包 installer，需 Inno Setup Compiler

README 指定的建置形態大致如下：

```sh
./reconf
mkdir build
cd build
../configure --sbindir=/Ser2Net/bin --libexecdir=/Ser2Net/bin --mandir=/Ser2Net/man \
    --includedir=/Ser2Net/include --prefix=/Ser2Net \
    CPPFLAGS=-I$HOME/install/Gensio/include LDFLAGS=-L$HOME/install/Gensio/lib
make
make install DESTDIR=$HOME/install
```

## 本機環境檢查與執行結果

初始檢查時，Windows/PowerShell 環境尚未具備 build prerequisites：

- `where.exe gcc`：未找到
- `where.exe autoreconf`：未找到
- `where.exe pkg-config`：未找到
- `Test-Path C:\msys64`：`False`
- `Test-Path C:\msys64\ucrt64\include\yaml.h`：`False`
- `Test-Path C:\msys64\ucrt64\include\gensio\gensio.h`：`False`

後續已完成以下安裝與驗證：

- 透過 `winget` 安裝 MSYS2。
- 透過 `pacman` 安裝 UCRT64 GCC、autotools、pkgconf、libyaml、OpenSSL。
- 從 `cminyard/gensio` 原始碼建置 gensio `v3.0.2-26-g0a003593`。
- 以 UCRT64 成功 configure/build ser2net `4.6.7`。
- 建立 `dist/Ser2Net` portable folder。
- 用 `ldd` 收集 UCRT64 runtime DLL、OpenSSL、libyaml 與 gensio DLL。
- 在一般 PowerShell 環境直接執行 `dist\Ser2Net\bin\ser2net.exe -v`，成功輸出版本。
- 使用 `smoke-echo.yaml` 啟動 TCP echo listener，透過 `TcpClient` 連線並讀回 `ping`。
- 安裝 Inno Setup Compiler 6.7.3，產出 `dist\installer\Ser2Net-4.6.7-win64.exe`。

## 平台相依性與風險

### 1. 不是 MSVC 友善專案

專案沒有 CMake、Meson 或 Visual Studio solution，主要建置流程是 autotools。雖然 MinGW/MSYS2 可處理這種專案，但 MSVC 直接編譯會遇到 POSIX API、autotools 與 Unix shell 工具鏈問題。

### 2. gensio 是最大外部風險

ser2net 大量功能建立在 gensio 上。`configure.ac` 直接要求 gensio 標頭與多個 gensio library symbol。若 gensio 未以相同 MSYS2 runtime 成功建置，ser2net 即使原始碼可相容也無法連結。

### 3. Linux-only 功能已大多有條件保護，但仍需確認 configure 結果

`led_sysfs.c` 是 Linux sysfs LED driver，但實作包在 `USE_SYSFS_LED_FEATURE` 內；`configure.ac` 只有在 Linux host 預設啟用。`dataxfer.h` 的 `<linux/serial.h>` 也包在 `#ifdef linux` 內。這些設計對 Windows build 是合理的，但仍需以實際 MinGW configure 結果確認沒有誤判 host macro。

### 4. Windows 路徑與執行配置不同

Windows 版不依賴 Unix 的 `/etc/ser2net` 與 `/usr/share/ser2net`。預設設定目錄改成 executable 相對路徑，因此安裝包需要放置：

- `{app}/bin/ser2net.exe`
- `{app}/etc/ser2net/ser2net.yaml`
- `{app}/share/ser2net`
- 所有必要 DLL

目前 `ser2net.iss` 只明確列出 `ser2net.exe` 與 `libyaml-0-2.dll`。實際打包時需要用 `ldd` 或 MSYS2 工具確認 gensio、OpenSSL、gcc runtime、pthread/winpthread、zlib 等 DLL 是否也需要一起帶入。

### 5. upstream 測試不等於 Windows 驗收

README 表示測試目前只在 Linux 執行，且需要 `serialsim` kernel module、gensio Python module 與 OpenIPMI `ipmi_sim`。因此 Windows 版即使能 build，也不能依賴既有 test suite 完成品質驗收，應建立 Windows smoke test。

## 建議的 Windows 驗證流程

1. 安裝 MSYS2，使用 UCRT64 shell。
2. 安裝必要套件：

```sh
pacman -S --needed base-devel git autoconf automake libtool \
    mingw-w64-ucrt-x86_64-gcc \
    mingw-w64-ucrt-x86_64-pkgconf \
    mingw-w64-ucrt-x86_64-libyaml
```

3. 先建置並安裝 Windows 版 gensio，安裝到 `$HOME/install/Gensio` 或改用套件版。
4. 在 ser2net repo 執行：

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

5. 檢查 DLL：

```sh
ldd $HOME/install/Ser2Net/bin/ser2net.exe
```

6. 建立最小 Windows 設定，例如先用 TCP-to-TCP 或 TCP-to-stdio 類型測試程序啟動，再測試實體 COM port：

```yaml
%YAML 1.1
---
connection: &com-test
  accepter: tcp,3001
  connector: serialdev,COM3,115200N81
```

7. 執行 smoke test：

```sh
ser2net -n -d -c ../etc/ser2net/ser2net.yaml
```

8. 從另一個 shell 連線測試：

```sh
gensiot tcp,localhost,3001
```

9. 用 Inno Setup Compiler 編譯 `ser2net.iss`，並確認 installer 內包含所有必要 DLL 與預設設定檔。

## 建議驗收標準

- `./reconf` 成功產生 `configure`
- `../configure` 成功找到 Windows 版 gensio 與 libyaml
- `make -j` 產生 `ser2net.exe`
- `ser2net.exe -v` 或等效版本查詢可執行
- `ser2net -n -d -c <config>` 可在 Windows console 前景啟動
- TCP accepter 可接受 localhost 連線
- `serialdev,COMx,...` 可開啟實體或虛擬 COM port
- RFC2217 模式可透過 gensiot 控制基本 serial parameter
- installer 安裝後，PATH、`etc/ser2net`、`share/ser2net` 與 DLL 均正確

## 最終判斷

此專案具備 Windows 版可編譯與可執行的設計基礎，且 upstream 文件已明示支援 UCRT64/MINGW64。若目標是「在 Windows 上可執行的 ser2net.exe 與安裝包」，建議採 MSYS2 UCRT64 路線，不建議投入 MSVC 原生 port，除非另有企業發行或工具鏈一致性的硬性需求。

目前本機已成功編譯、建立 portable folder，並產出 Inno Setup installer。尚未完成的部分是乾淨 Windows VM 安裝驗證，以及實體或虛擬 COM port 的 serialdev 測試。
