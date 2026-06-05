# GitHub Actions Windows Package

This repository includes `.github/workflows/windows-package.yml` to build and publish Windows packages in CI.

## What It Builds

- MSYS2/UCRT64 build of `gensio`
- MSYS2/UCRT64 build of `ser2net.exe`
- Framework-dependent portable folder at `dist/Ser2Net-framework`
- Self-contained portable folder at `dist/Ser2Net-runtime`
- Framework-dependent Inno Setup installer at `dist/installer/Ser2Net-<version>-win64.exe`
- Self-contained Inno Setup installer at `dist/installer/Ser2Net-<version>-win64-runtime.exe`
- Framework-dependent portable zip at `dist/installer/Ser2Net-<version>-win64-portable.zip`
- Self-contained portable zip at `dist/installer/Ser2Net-<version>-win64-runtime-portable.zip`

## When It Runs

- Push to `main`, `master`, or `win64`
- Pull request
- Manual `workflow_dispatch`
- Tags matching `v*`

## Artifacts

Every successful run uploads a GitHub Actions artifact named:

```text
Ser2Net-<version>-win64
```

The artifact contains:

- `Ser2Net-<version>-win64.exe`
- `Ser2Net-<version>-win64-runtime.exe`
- `Ser2Net-<version>-win64-portable.zip`
- `Ser2Net-<version>-win64-runtime-portable.zip`
- `dll-inventory.txt`

## Release Publishing

When the workflow runs from a tag matching `v*`, it also uploads the installers and portable zips to the GitHub Release for that tag.

Example:

```sh
git tag v4.6.7-win64
git push origin v4.6.7-win64
```

## Gensio Source

The workflow builds `gensio` from `https://github.com/cminyard/gensio.git`.

By default it uses the gensio commit that was validated during local Windows packaging:

```yaml
GENSIO_REF: 0a00359305f211dbe844ec7eaa6962c7f1af52b1
```

Change this value in `.github/workflows/windows-package.yml` to pin a specific gensio tag or commit.

## Code Signing Preparation

The SignPath Foundation application preparation is tracked in:

- `docs/signpath-foundation-application.md`
- `docs/code-signing-policy.md`
- `docs/privacy.md`

After SignPath approval, the workflow should sign Ser2Net project binaries before packaging and sign the final Inno Setup installers before upload. Upstream DLLs may be included unsigned, but should not be signed as Ser2Net project binaries unless they are built and maintained by this repository.
