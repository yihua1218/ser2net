# SignPath Foundation Application Preparation

This document collects the information needed before applying for a free
SignPath.io subscription and SignPath Foundation certificate.

## Application Summary

Project name: Ser2Net

Project type: Open-source Windows installer and portable package for ser2net,
a configurable bridge between gensio accepters and gensio connectors. Common
uses include exposing local serial ports or IPMI Serial-over-LAN connections
through configured network endpoints.

Repository: `https://github.com/cminyard/ser2net`

Primary Windows artifacts:

- `Ser2Net-<version>-win64.exe`
- `Ser2Net-<version>-win64-runtime.exe`
- `Ser2Net-<version>-win64-portable.zip`
- `Ser2Net-<version>-win64-runtime-portable.zip`

Build workflow: `.github/workflows/windows-package.yml`

Code signing policy: `docs/code-signing-policy.md`

Privacy policy: `docs/privacy.md`

## Eligibility Checklist

- OSS license: confirm that all signed project code is under the repository's
  open-source license and that bundled upstream libraries are open source or
  system libraries.
- Maintained project: confirm that the repository has active maintainers and
  recent release activity.
- Released project: publish at least one unsigned Windows package in the same
  form that should later be signed.
- Documented download page: make the GitHub Release page describe the Windows
  installer, portable packages, system changes, and uninstall behavior.
- Code signing policy: link `docs/code-signing-policy.md` from the project home
  page and Windows release/download page.
- Privacy policy: link `docs/privacy.md` from the code signing policy and
  release/download page.
- MFA: require multi-factor authentication for repository and SignPath access
  for committers, reviewers, and approvers.
- Metadata: ensure signed binaries and installers use the `Ser2Net` product
  name and a single release version.

## Release Page Text

Use this text on the GitHub Release page for Windows artifacts:

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

After SignPath approval, add:

```markdown
Free code signing provided by SignPath.io, certificate by SignPath Foundation.
```

## Suggested Application Answers

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

## Integration TODOs After Approval

- Create the SignPath project and artifact configuration.
- Add SignPath API credentials to GitHub Actions secrets.
- Add a signing step for `ser2net.exe`, `Ser2Net.Service.exe`, and
  `Ser2Net.Tray.exe` before packaging installers and zip files.
- Add a signing step for the final Inno Setup installers.
- Verify signed files with `signtool verify /pa /v`.
- Keep unsigned upstream DLLs included but do not sign them as Ser2Net project
  binaries unless they are built and maintained by this repository.
