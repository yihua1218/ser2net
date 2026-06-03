# ser2net Windows Portable Folder and Inno Setup Installer Execution Plan

Plan date: 2026-06-03
Target platform: Windows 10/11 x64
Build environment: MSYS2 UCRT64 preferred, MINGW64 as fallback
Delivery goal: allow colleagues to deploy and run ser2net through an installer without installing MSYS2/MinGW.

## Goal

Create a verified Windows portable folder containing `ser2net.exe`, all required DLLs, the default configuration file, and data directories, then package that folder into an `.exe` installer with Inno Setup.

The first version does not pursue a fully static executable and does not install a Windows Service. The goal is to reduce deployment friction, improve reproducibility, and leave room for a service installer in a later phase.

## Deliverables

| Deliverable          | Description                                                                           |
|----------------------|---------------------------------------------------------------------------------------|
| Portable folder      | A `Ser2Net/` directory that can be copied directly to another Windows machine and run |
| Inno Setup installer | `Ser2Net-<version>-win64.exe` for colleagues to install                               |
| DLL inventory        | Records each bundled DLL and its purpose/source                                       |
| Smoke test record    | Proves that the installed package can start and accept a TCP connection               |
| Updated `.iss`       | Reproducible Inno Setup script for generating the installer                           |

## Recommended Directory Layout

```text
Ser2Net/
  bin/
    ser2net.exe
    libyaml-0-2.dll
    libgensio*.dll
    libssl*.dll
    libcrypto*.dll
    libwinpthread-1.dll
    other required DLLs found by ldd
  etc/
    ser2net/
      ser2net.yaml
  share/
    ser2net/
  docs/
    README-Windows.md
```

On Windows, `ser2net.c` derives `../etc/ser2net` and `../share/ser2net` relative to the executable. The installer should therefore place `ser2net.exe` in `{app}\bin` and the configuration file in `{app}\etc\ser2net`.

## Work Phases

### Phase 1: Prepare the Build Environment

Work items:

- Install MSYS2.
- Use the UCRT64 shell as the primary build shell.
- Install autotools, GCC, pkgconf, and libyaml.
- Prepare Windows gensio development headers and libraries.
- Confirm that `gcc`, `autoreconf`, `pkg-config`, `yaml.h`, and `gensio/gensio.h` can all be found.

Recommended command:

```sh
pacman -S --needed base-devel git autoconf automake libtool \
    mingw-w64-ucrt-x86_64-gcc \
    mingw-w64-ucrt-x86_64-pkgconf \
    mingw-w64-ucrt-x86_64-libyaml
```

Acceptance:

- `gcc --version` runs.
- `autoreconf --version` runs.
- `pkg-config --modversion yaml-0.1` returns a version.
- gensio headers and libraries exist in a fixed installation location.

### Phase 2: Build gensio

Work items:

- Build gensio with the same MSYS2 runtime.
- Prefer shared libraries so the build works cleanly with a portable DLL bundle.
- Record enabled gensio features, such as SSL, mDNS, and IPMI.
- Install to a fixed staging prefix, such as `$HOME/install/Gensio`.

Acceptance:

- `$HOME/install/Gensio/include/gensio/gensio.h` exists.
- `$HOME/install/Gensio/lib` contains gensio import/static libraries.
- `$HOME/install/Gensio/bin`, or the MSYS2 runtime, contains the corresponding DLLs.
- `pkg-config` or `CPPFLAGS` / `LDFLAGS` allow ser2net to find gensio.

Risks:

- Enabling too many gensio features increases the DLL count and deployment complexity.
- If gensio and ser2net use different MSYS2 runtimes, link or runtime failures may occur.

### Phase 3: Build ser2net

Work items:

- Run `./reconf` to generate the autotools build infrastructure.
- Create a separate build directory.
- Use the Windows prefix settings recommended by the README.
- Build and install into a staging directory.

Recommended command:

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

Acceptance:

- `$HOME/install/Ser2Net/bin/ser2net.exe` exists.
- `ser2net.exe` can print its version or usage information.
- `configure` does not enable Linux sysfs LED support.
- `make` has no unresolved symbols or missing DLL problems.

### Phase 4: Create the Portable Folder

Work items:

- Create a clean `dist/Ser2Net/` staging directory.
- Copy `ser2net.exe` to `dist/Ser2Net/bin/`.
- Copy the default `ser2net.yaml` to `dist/Ser2Net/etc/ser2net/`.
- Create `dist/Ser2Net/share/ser2net/`.
- Use `ldd` to scan runtime DLLs for `ser2net.exe`.
- Copy required DLLs to `dist/Ser2Net/bin/`.
- Create a DLL inventory document.

Recommended command:

```sh
ldd dist/Ser2Net/bin/ser2net.exe
```

DLL inclusion rules:

- Include non-system DLLs directly required by ser2net.
- Include non-system DLLs required by gensio.
- Include libyaml, OpenSSL, winpthread, GCC runtime, and other required DLLs.
- Do not include Windows system DLLs such as `KERNEL32.dll` or `WS2_32.dll`.

Acceptance:

- `dist/Ser2Net/` can be copied to a Windows environment without MSYS2 in PATH and still start.
- Running `ser2net.exe -h` or an equivalent command from `dist/Ser2Net/bin` succeeds.
- The default or smoke test configuration can start a foreground process.

### Phase 5: Smoke Test

Work items:

- Create a minimal test configuration.
- Run ser2net in the foreground from a Windows console.
- Verify that the TCP accepter can accept localhost connections.
- Test one physical or virtual COM port.
- If RFC2217 is required, test basic serial parameter control.

Recommended minimal configuration:

```yaml
%YAML 1.1
---
connection: &com-test
  accepter: tcp,3001
  connector: serialdev,COM3,115200N81
```

Recommended startup command:

```sh
ser2net -n -d -c ..\etc\ser2net\ser2net.yaml
```

Acceptance:

- ser2net starts from a Windows command prompt without an MSYS2 shell.
- `localhost:3001` accepts connections.
- The specified COM port can be opened.
- Error messages clearly identify configuration or DLL problems.

### Phase 6: Update the Inno Setup Script

Work items:

- Change the `.iss` input source to the portable folder staging directory.
- Include `bin`, `etc`, `share`, and `docs` through wildcards or explicit lists.
- Include the version and platform in the installer output filename.
- Keep PATH update behavior, but verify uninstall removes PATH changes.
- Add a Start Menu shortcut.
- Optional: add an option to open the README or start a console after installation.

Suggested `.iss` concept:

```ini
[Files]
Source: "dist\Ser2Net\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "dist\Ser2Net\etc\*"; DestDir: "{app}\etc"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dist\Ser2Net\share\*"; DestDir: "{app}\share"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dist\Ser2Net\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
```

Acceptance:

- The installer can be installed on a clean Windows VM.
- `{app}\bin\ser2net.exe` exists after installation.
- `{app}\etc\ser2net\ser2net.yaml` exists after installation.
- PATH has no duplicate or stale entry after uninstall.

### Phase 7: Clean Environment Validation

Work items:

- Prepare a Windows 10/11 x64 machine without MSYS2/MinGW in PATH.
- Run the installer.
- Open a new command prompt.
- Verify that `ser2net.exe` can run from PATH or directly from `{app}\bin`.
- Run the smoke test.
- Uninstall and confirm that files and PATH changes are cleaned up.

Acceptance:

- MSYS2 is not required.
- No manual DLL copying is required.
- ser2net starts after installation.
- Uninstall does not damage the existing PATH.

## Milestones

| Milestone                           | Completion condition                                              |
|-------------------------------------|-------------------------------------------------------------------|
| M1: Toolchain usable                | MSYS2 UCRT64, libyaml, and gensio build environment are available |
| M2: ser2net builds                  | `ser2net.exe` is produced and can start on the build host         |
| M3: Portable folder runs            | `dist/Ser2Net/` runs without MSYS2 in PATH                        |
| M4: Installer installs              | Inno Setup produces an installer that installs to `{app}`         |
| M5: Clean machine validation passes | Smoke test passes on Windows without MSYS2                        |

## Risks and Mitigations

| Risk                                           | Impact                                               | Mitigation                                                                          |
|------------------------------------------------|------------------------------------------------------|-------------------------------------------------------------------------------------|
| Missing DLL in package                         | Colleague machine cannot start the program           | Build a DLL inventory with `ldd` and validate on a clean VM                         |
| Too many gensio feature dependencies           | Installer grows and DLL list becomes complex         | Keep only required functionality in the first version, then add SSL/mDNS/IPMI later |
| COM port behavior differs from Linux serialdev | Real device connection fails                         | Run smoke tests with a physical or virtual COM port                                 |
| PATH modification pollutes system state        | Poor install/uninstall experience                    | Check for duplicates before adding PATH and verify uninstall cleanup                |
| Windows Service added too early                | More permission, account, and maintenance complexity | First version remains a console app; service support moves to phase two             |
| Fully static linking is not feasible           | Delivery is delayed                                  | Exclude it from the first version and use a portable DLL bundle                     |

## Out of Scope for the First Version

- Fully static single executable.
- Automatic Windows Service installation and startup.
- GUI configuration tool.
- Automatic COM port detection and configuration generation.
- Code-level Windows port refactoring.
- Full migration of the upstream Linux test suite.

## First-Version Success Definition

The first version is complete when colleagues can receive an installer, install it on Windows 10/11 x64 without MSYS2/MinGW, start ser2net directly, use a default or specified `ser2net.yaml` to open a TCP accepter, and connect it to a specified COM port.

At that point, the package is ready for internal distribution and testing. Follow-up work can decide whether to add Windows Service support, signing, a versioned release pipeline, or a fully static linking attempt.
