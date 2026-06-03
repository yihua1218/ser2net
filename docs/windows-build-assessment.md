# ser2net Windows Build and Runtime Feasibility Assessment

Assessment date: 2026-06-03
Assessment scope: source code, build configuration, documentation, and local toolchain status in `d:\Workspace\ser2net`.

## Conclusion

This project can be built into a Windows executable, but the target environment is not a native Visual Studio/MSVC build. The supported path is MSYS2 UCRT64 or MINGW64. The project README explicitly includes a Windows Support section, states that a Windows build of gensio and `mingw-w64-x86_64-libyaml` are required, recommends building under UCRT64/MINGW64, and notes that `ser2net.iss` can be used to create an Inno Setup installer.

MSYS2 UCRT64, autotools, GCC, libyaml, OpenSSL, gensio, and Inno Setup Compiler were later installed locally. A real build, portable folder, and installer packaging pass were completed. The assessment has therefore moved from static feasibility review to a locally verified build.

## Support Summary

| Item                      | Assessment                                                                                                      |
|---------------------------|-----------------------------------------------------------------------------------------------------------------|
| Windows build feasibility | Feasible, but requires MSYS2 UCRT64/MINGW64                                                                     |
| Native MSVC build         | Not recommended; the project does not provide CMake, a Visual Studio solution, or an MSVC-compatible build flow |
| Windows runtime form      | Standard Windows console executable, using `etc/ser2net` and `share/ser2net` relative to the exe                |
| Installer                 | Inno Setup script `ser2net.iss` is available                                                                    |
| Test coverage             | Upstream tests are mainly Linux-only; Windows needs separate smoke/integration tests                            |
| Main risks                | gensio Windows build, complete DLL packaging, serialdev/COM port behavior validation                            |

## Evidence of Windows Support in the Project

1. The README Windows Support section explicitly states that ser2net can be built for Windows and specifies UCRT64/MINGW64 plus `mingw-w64-x86_64-libyaml`.
   - Source: `README.rst:315-321`

2. The README explains that Windows does not use Unix `sysconfdir` / `datarootdir`; instead it uses paths relative to the executable: `../etc/ser2net` and `../share/ser2net`.
   - Source: `README.rst:323-325`

3. The README provides a Windows installation path configuration example and states that `ser2net.iss` can be used to create an executable installer.
   - Source: `README.rst:327-338`

4. `ser2net.c` has `_WIN32` conditional compilation and uses `GetModuleFileNameA()` to locate the executable and derive default Windows data directories.
   - Source: `ser2net.c:88-116`

5. `ser2net.c` wraps syslog, pidfile cleanup, daemon detach/fork/setsid, and other POSIX behavior in `#ifndef WIN32`, avoiding direct use of these Unix-only APIs in Windows builds.
   - Source: `ser2net.c:260-287`, `ser2net.c:527-577`

6. `ser2net.iss` already existed and packaged `ser2net.exe` plus `libyaml-0-2.dll` into `{app}/bin`, while adding that path to PATH.
   - Source: `ser2net.iss:8-9`, `ser2net.iss:36-38`

## Build System and Dependencies

The project uses autotools:

- `reconf` runs `libtoolize`, `aclocal`, `autoconf`, and `automake -a`.
- `configure.ac` checks for `gensio/gensio.h`, `libgensio`, `libgensioosh`, `libgensiomdns`, `yaml.h`, and `libyaml`.
- `Makefile.am` builds the main sources into a single `ser2net` program and includes the `tests` subdirectory.

Required prerequisites:

- MSYS2 UCRT64 or MINGW64 shell
- `base-devel`
- `autoconf`
- `automake`
- `libtool`
- UCRT64/MINGW64 `gcc`
- UCRT64/MINGW64 `libyaml`
- Windows gensio development headers and libraries
- Inno Setup Compiler, if creating an installer

The README-recommended build shape is roughly:

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

## Local Environment Check and Execution Results

At the initial check, the Windows/PowerShell environment did not yet have the build prerequisites:

- `where.exe gcc`: not found
- `where.exe autoreconf`: not found
- `where.exe pkg-config`: not found
- `Test-Path C:\msys64`: `False`
- `Test-Path C:\msys64\ucrt64\include\yaml.h`: `False`
- `Test-Path C:\msys64\ucrt64\include\gensio\gensio.h`: `False`

The following installation and validation steps were completed later:

- Installed MSYS2 via `winget`.
- Installed UCRT64 GCC, autotools, pkgconf, libyaml, and OpenSSL via `pacman`.
- Built gensio `v3.0.2-26-g0a003593` from `cminyard/gensio` source.
- Successfully configured and built ser2net `4.6.7` under UCRT64.
- Created a `dist/Ser2Net` portable folder.
- Used `ldd` to collect UCRT64 runtime DLLs, OpenSSL, libyaml, and gensio DLLs.
- Ran `dist\Ser2Net\bin\ser2net.exe -v` directly from a normal PowerShell environment and successfully printed the version.
- Started a TCP echo listener using `smoke-echo.yaml`, connected with `TcpClient`, and read back `ping`.
- Installed Inno Setup Compiler 6.7.3 and produced `dist\installer\Ser2Net-4.6.7-win64.exe`.

## Platform Dependencies and Risks

### 1. This Is Not an MSVC-Friendly Project

The project does not provide CMake, Meson, or a Visual Studio solution. Its primary build flow is autotools. MinGW/MSYS2 can handle this project, but direct MSVC compilation would run into POSIX APIs, autotools, and Unix shell toolchain requirements.

### 2. gensio Is the Largest External Risk

ser2net relies heavily on gensio. `configure.ac` directly requires gensio headers and several gensio library symbols. If gensio is not built successfully with the same MSYS2 runtime, ser2net may still be source-compatible but fail to link or run.

### 3. Linux-Only Features Are Mostly Guarded, but Configure Results Still Matter

`led_sysfs.c` is a Linux sysfs LED driver, but its implementation is guarded by `USE_SYSFS_LED_FEATURE`; `configure.ac` enables it by default only on Linux hosts. The `<linux/serial.h>` include in `dataxfer.h` is also guarded by `#ifdef linux`. These are reasonable for Windows builds, but the actual MinGW configure result should still be checked to ensure host macros are not misdetected.

### 4. Windows Paths and Runtime Layout Differ

The Windows build does not use Unix `/etc/ser2net` or `/usr/share/ser2net`. Default configuration paths are derived relative to the executable, so the installer must place:

- `{app}/bin/ser2net.exe`
- `{app}/etc/ser2net/ser2net.yaml`
- `{app}/share/ser2net`
- all required DLLs

The original `ser2net.iss` explicitly packaged only `ser2net.exe` and `libyaml-0-2.dll`. Real packaging must use `ldd` or MSYS2 tooling to confirm whether gensio, OpenSSL, GCC runtime, pthread/winpthread, zlib, and other DLLs also need to be included.

### 5. Upstream Tests Are Not Windows Acceptance Tests

The README states that the tests currently run only on Linux and require the `serialsim` kernel module, the gensio Python module, and OpenIPMI `ipmi_sim`. Therefore, even if the Windows build succeeds, the existing test suite is not enough for Windows quality acceptance. A Windows smoke test should be maintained separately.

## Recommended Windows Validation Flow

1. Install MSYS2 and use the UCRT64 shell.
2. Install required packages:

```sh
pacman -S --needed base-devel git autoconf automake libtool \
    mingw-w64-ucrt-x86_64-gcc \
    mingw-w64-ucrt-x86_64-pkgconf \
    mingw-w64-ucrt-x86_64-libyaml
```

3. Build and install the Windows version of gensio first, either under `$HOME/install/Gensio` or through a package if available.
4. In the ser2net repo, run:

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

5. Check DLL dependencies:

```sh
ldd $HOME/install/Ser2Net/bin/ser2net.exe
```

6. Create a minimal Windows configuration. Start with TCP-to-TCP or TCP-to-stdio if possible, then test a physical COM port:

```yaml
%YAML 1.1
---
connection: &com-test
  accepter: tcp,3001
  connector: serialdev,COM3,115200N81
```

7. Run a smoke test:

```sh
ser2net -n -d -c ../etc/ser2net/ser2net.yaml
```

8. Connect from another shell:

```sh
gensiot tcp,localhost,3001
```

9. Compile `ser2net.iss` with Inno Setup Compiler and verify that the installer contains all required DLLs and the default configuration file.

## Recommended Acceptance Criteria

- `./reconf` successfully generates `configure`.
- `../configure` successfully finds Windows gensio and libyaml.
- `make -j` produces `ser2net.exe`.
- `ser2net.exe -v` or an equivalent version query runs successfully.
- `ser2net -n -d -c <config>` starts in the foreground in a Windows console.
- A TCP accepter can receive localhost connections.
- `serialdev,COMx,...` can open a physical or virtual COM port.
- RFC2217 mode can control basic serial parameters through gensiot.
- After installer deployment, PATH, `etc/ser2net`, `share/ser2net`, and DLLs are correct.

## Final Assessment

This project has a valid design basis for building and running on Windows, and upstream documentation explicitly supports UCRT64/MINGW64. If the target is a Windows `ser2net.exe` and installer, the recommended path is MSYS2 UCRT64. A native MSVC port is not recommended unless there is a hard enterprise distribution or toolchain consistency requirement.

The local machine has successfully built ser2net, created a portable folder, and produced an Inno Setup installer. Remaining validation work is a clean Windows VM installation test and serialdev testing with a physical or virtual COM port.
