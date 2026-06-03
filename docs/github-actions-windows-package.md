# GitHub Actions Windows Package

This repository includes `.github/workflows/windows-package.yml` to build and publish Windows packages in CI.

## What It Builds

- MSYS2/UCRT64 build of `gensio`
- MSYS2/UCRT64 build of `ser2net.exe`
- Portable folder at `dist/Ser2Net`
- Inno Setup installer at `dist/installer/Ser2Net-<version>-win64.exe`
- Portable zip at `dist/installer/Ser2Net-<version>-win64-portable.zip`

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
- `Ser2Net-<version>-win64-portable.zip`
- `dll-inventory.txt`

## Release Publishing

When the workflow runs from a tag matching `v*`, it also uploads the installer and portable zip to the GitHub Release for that tag.

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
