# GitHub Actions Windows Package

這個 repository 已加入 `.github/workflows/windows-package.yml`，用來在 CI 裡編譯並發佈 Windows package。

## 產出內容

- MSYS2/UCRT64 編譯的 `gensio`
- MSYS2/UCRT64 編譯的 `ser2net.exe`
- `dist/Ser2Net` portable folder
- `dist/installer/Ser2Net-<version>-win64.exe` Inno Setup 安裝程式
- `dist/installer/Ser2Net-<version>-win64-portable.zip` portable zip

## 觸發條件

- Push 到 `main`、`master` 或 `win64`
- Pull request
- 手動執行 `workflow_dispatch`
- 符合 `v*` 的 tag

## Artifacts

每次成功執行都會上傳一個 GitHub Actions artifact：

```text
Ser2Net-<version>-win64
```

artifact 內容包含：

- `Ser2Net-<version>-win64.exe`
- `Ser2Net-<version>-win64-portable.zip`
- `dll-inventory.txt`

## Release 發佈

當 workflow 是由符合 `v*` 的 tag 觸發時，會把 installer 和 portable zip 上傳到該 tag 對應的 GitHub Release。

範例：

```sh
git tag v4.6.7-win64
git push origin v4.6.7-win64
```

## Gensio 來源

workflow 會從 `https://github.com/cminyard/gensio.git` 編譯 `gensio`。

預設使用本機 Windows package 驗證過的 gensio commit：

```yaml
GENSIO_REF: 0a00359305f211dbe844ec7eaa6962c7f1af52b1
```

如果要固定 gensio 版本，可以在 `.github/workflows/windows-package.yml` 把這個值改成特定 tag 或 commit。
