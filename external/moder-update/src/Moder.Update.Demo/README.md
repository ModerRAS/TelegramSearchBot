# Moder.Update.Demo

用于测试 Moder.Update 双进程更新流程的示例控制台应用程序。

## 前置要求

- .NET 10.0 SDK 或更高版本
- Windows 操作系统（该库使用 Win32 `ReplaceFile` API）

## 快速创建版本链

一条命令创建完整的版本链和所有更新包：

```bash
# 1. 发布基础版本
dotnet publish src/Moder.Update.Demo -c Release -o ./test-app

# 2. 创建版本链 (1.0.0 -> 1.1.0 -> ... -> 1.10.0) 和所有更新包
dotnet run --project src/Moder.Update.Demo -- \
    --create-version-chain ./test-app 1.0.0 10 ./demo-packages
```

这会自动创建：
- 10 个版本的更新包
- 完整的 `catalog.json`
- 从 1.0.0 到 1.10.0 的所有递增更新

## 手动测试流程

### 步骤 1：构建解决方案

```bash
dotnet build src/Moder.Update.sln
```

### 步骤 2：发布旧版本（1.0.0）

```bash
mkdir -p ./test-app
dotnet publish src/Moder.Update.Demo -c Release -o ./test-app
```

### 步骤 3：创建更新包

方式 A：使用 `--create-version-chain` 自动创建版本链

```bash
dotnet run --project src/Moder.Update.Demo -- \
    --create-version-chain ./test-app 1.0.0 5 ./demo-packages
```

方式 B：手动创建单个包

```bash
# 先准备好新版本文件到某个目录，然后
dotnet run --project src/Moder.Update.Demo -- \
    --create-package 1.0.0 1.1.0 ./新版本目录 ./demo-packages
```

### 步骤 4：测试更新检测

```bash
dotnet .\test-app\Moder.Update.Demo.dll --version
# 输出: Current version: 1.0.0

dotnet .\test-app\Moder.Update.Demo.dll --check
# 输出:
# Checking for updates from version 1.0.0...
# Packages directory: /path/to/demo-packages
# Update available! Latest version: 1.x.x
# Update path:
#   1.0.0 -> 1.1.0 (update-1.0.0-to-1.1.0.zst)
#   1.1.0 -> 1.2.0 (update-1.1.0-to-1.2.0.zst)
#   ...
```

### 步骤 5：应用更新

```bash
dotnet .\test-app\Moder.Update.Demo.dll --apply
```

## 命令

| 命令 | 描述 |
|------|------|
| `--version` | 显示当前版本 |
| `--check` | 使用本地目录检查更新 |
| `--apply` | 应用更新包并重启 |
| `--create-package <from> <to> <source> <output>` | 创建单个测试更新包 |
| `--create-version-chain <baseDir> <startVer> <count> <outputDir>` | 创建版本链和所有更新包 |

## 工作原理

1. `--check` 使用 `UpdateChecker` 从本地 `demo-packages/` 目录获取 `catalog.json`
2. `--apply` 下载 `.zst` 包并调用 `UpdateManager.ApplyUpdateAsync()`
3. 应用后，`PrepareRestart()` 启动 `Moder.Update.Updater.exe` 并退出应用
4. 更新进程等待应用退出，替换文件，验证校验和，然后重启

## 包格式

更新包使用 Moder.Update 的二进制格式：
- 4 字节魔术头：`MUP\0`
- Zstd 压缩的 tar 归档，包含：
  - `manifest.json` — 版本信息、文件列表、校验和
  - 要替换的应用程序文件

## 故障排除

**"Updater not found" 警告**：更新进程二进制文件应在 `src/updater/target/release/moder_update_updater`。如果缺失，请运行 `cargo build --release` 构建。

**"No update available"**：确保 `demo-packages/catalog.json` 存在，且包含匹配 `minSourceVersion` 的条目。

**版本链创建失败**：确保基础目录包含要打包的文件（不能是空目录）。
