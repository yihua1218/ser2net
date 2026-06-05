# Code Signing Policy

This policy describes how Windows release artifacts for Ser2Net are built,
reviewed, approved, and signed.

Free code signing provided by [SignPath.io](https://about.signpath.io/),
certificate by [SignPath Foundation](https://signpath.org/).

## Scope

This policy applies to Windows release artifacts produced by
`.github/workflows/windows-package.yml`, including:

- `Ser2Net-<version>-win64.exe`
- `Ser2Net-<version>-win64-runtime.exe`
- `Ser2Net-<version>-win64-portable.zip`
- `Ser2Net-<version>-win64-runtime-portable.zip`

The installers package `ser2net.exe`, the Windows service wrapper,
the Windows tray manager, required open-source runtime libraries, default
configuration files, and documentation.

## Team Roles

Committers and reviewers are the maintainers with write access to the source
repository. Pull requests from people without commit access must be reviewed
before merge.

Approvers are the maintainers who are allowed to approve SignPath signing
requests for Windows release artifacts. A signing request must only be approved
after confirming that the artifacts were produced by the repository's automated
release workflow for the intended source revision or tag.

All team members responsible for committing, reviewing, or approving signing
requests must use multi-factor authentication for repository access and
SignPath access.

## Build Provenance

Windows release artifacts are built from source by GitHub Actions. The workflow
checks out the repository, builds the MSYS2/UCRT64 `ser2net.exe`, publishes the
.NET Windows service and tray tools, validates required package contents, builds
the Inno Setup installers, creates portable zip files, and uploads release
assets for version tags.

Signing approval must not be granted for artifacts produced outside the
documented release workflow.

## What May Be Signed

Only Ser2Net project binaries and installers built from this repository may be
signed through this policy.

Unsigned open-source upstream binaries and runtime libraries may be included in
the installers or portable zip files when required for the Windows package, but
they must not be signed as Ser2Net project binaries unless they are built and
maintained as part of this repository.

## Privacy

See [Privacy Policy](privacy.md).

Ser2Net does not transfer information to networked systems unless specifically
requested by the user or the person installing or operating it. The software is
designed to bridge configured local serial, console, or gensio endpoints to
configured network endpoints.

## System Changes

The Windows installer may:

- install files under `C:\Program Files\Ser2Net`
- create writable configuration and log directories under `C:\ProgramData\Ser2Net`
- optionally install and start the `Ser2Net` Windows service
- optionally start the Ser2Net tray manager when Windows starts
- add the installed `bin` directory to the system `PATH`

The installer also provides uninstall actions that stop and remove the Windows
service and remove the installed `bin` directory from the system `PATH`.

## Release Approval Checklist

Before approving a SignPath signing request, the approver should confirm:

- The signing request corresponds to an intended release tag or approved
  workflow run.
- The source revision matches the intended release.
- The Windows package workflow completed successfully.
- Required artifacts are present.
- Binary product names and product versions are set consistently.
- The release notes or download page describe what the Windows package does.
- No proprietary components were added to the signed package.
