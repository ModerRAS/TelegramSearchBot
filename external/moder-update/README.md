> ⚠️ 本文件是合并到 TelegramSearchBot 仓库的副本。
> 如需更新原项目，请将相关修改同步到 https://github.com/ModerRAS/Moder.Update

# Moder.Update

一个 .NET 双进程自更新库。应用程序可以下载更新、启动独立的更新进程、退出，让更新进程原子性地替换文件并重启。

## 功能特性

- **双进程更新模式** — 主程序下载更新、启动更新进程、退出；更新进程替换文件并重启主程序
- **Zstd 压缩** — 更新包使用 Zstd（通过 ZstdSharp）进行高效压缩
- **原子性文件替换** — 在 Windows 上使用 `ReplaceFile` Win32 API 进行可靠的文件替换
- **回滚支持** — 失败时自动备份和回滚
- **链式更新路径** — 支持锚点包和累积包的版本链
- **SHA512 校验** — 文件级完整性检查
- **无内置 HTTP** — 消费者通过 `IUpdateCatalogFetcher` 提供自己的 HTTP 实现
- **无界面** — 纯库，通过事件报告进度，无 UI

## 入门指南

### 安装

```bash
dotnet add package Moder.Update
```

### 基本用法

```csharp
using Moder.Update;
using Moder.Update.Compression;
using Moder.Update.FileOperations;
using Moder.Update.Models;
using Moder.Update.Package;

// 设置组件
var compressor = new ZstdCompressor();
var packageReader = new ZstdPackageReader(compressor);
var fileService = new FileReplacementService();
var processSpawner = new ProcessSpawner();
var updateManager = new UpdateManager(packageReader, fileService, processSpawner);

// 检查更新（使用你的 HTTP 客户端实现 IUpdateCatalogFetcher）
var checker = new UpdateChecker(myFetcher, updateManager);
var result = await checker.CheckForUpdatesAsync("1.0.0");

if (result.Status == UpdateCheckStatus.UpdateAvailable)
{
    foreach (var entry in result.UpdatePath!)
    {
        // 下载并应用每个更新包
        using var packageStream = await myFetcher.DownloadPackageAsync(entry.PackagePath);
        var updateResult = await updateManager.ApplyUpdateAsync(packageStream, new UpdateOptions
        {
            CurrentVersion = "1.0.0",
            TargetDir = AppContext.BaseDirectory,
            EnableRollback = true
        });
    }

    // 启动更新进程并退出
    updateManager.PrepareRestart("Moder.Update.Updater.exe", options);
    Environment.Exit(0);
}
```

### 实现 IUpdateCatalogFetcher

```csharp
public class MyHttpFetcher : IUpdateCatalogFetcher
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;

    public MyHttpFetcher(HttpClient client, string baseUrl)
    {
        _client = client;
        _baseUrl = baseUrl;
    }

    public async Task<string> FetchCatalogAsync(CancellationToken ct = default)
    {
        return await _client.GetStringAsync($"{_baseUrl}/catalog.json", ct);
    }

    public async Task<Stream> DownloadPackageAsync(string packagePath, CancellationToken ct = default)
    {
        return await _client.GetStreamAsync($"{_baseUrl}/{packagePath}", ct);
    }
}
```

## 包格式

更新包使用以下二进制格式：

```
[4 字节: "MUP\0" 魔术头] + [Zstd 压缩的 tar 归档]
```

tar 归档包含：
- `manifest.json` — 更新清单，包含版本信息、文件列表和校验和
- 应用程序文件 — 完整替换文件（非差分）

## 更新目录

更新目录是一个 JSON 文件，服务于固定 URL：

```json
{
  "latestVersion": "2.0.0",
  "minRequiredVersion": "1.0.0",
  "lastUpdated": "2025-01-01T00:00:00Z",
  "entries": [
    {
      "packagePath": "packages/1.1.0.zst",
      "targetVersion": "1.1.0",
      "minSourceVersion": "1.0.0",
      "maxSourceVersion": "1.0.99",
      "packageChecksum": "sha512hash...",
      "fileCount": 5,
      "compressedSize": 1024000,
      "uncompressedSize": 2048000
    }
  ]
}
```

## 更新进程

`Moder.Update.Updater` 是一个独立的控制台应用程序：

1. 等待主应用程序进程退出
2. 使用原子操作替换应用程序文件
3. 重启主应用程序

```bash
Moder.Update.Updater --target-pid 1234 --target-path /path/to/app --staging-dir /path/to/staging
```

## 构建

```bash
dotnet build src/Moder.Update.sln
dotnet test src/Moder.Update.sln
```

## 打包

```bash
./scripts/pack.sh   # Linux/macOS
scripts\pack.cmd     # Windows
```

## 示例程序

提供了一个示例项目（`Moder.Update.Demo`）用于端到端测试更新流程。

### 前置要求

- .NET 10.0 SDK 或更高版本
- Windows 操作系统（该库使用 Win32 `ReplaceFile` API）

### 快速开始

```bash
# 1. 构建解决方案
dotnet build src/Moder.Update.sln

# 2. 创建测试包 (1.0.0 -> 1.1.0)
dotnet run --project src/Moder.Update.Demo -- \
    --create-package 1.0.0 1.1.0 ./demo-app ./demo-packages

# 3. 将示例程序复制到测试目录
mkdir -p ./test-app
dotnet publish src/Moder.Update.Demo -c Release -o ./test-app

# 4. 运行 --check 查看更新状态
dotnet run --project ./test-app/Moder.Update.Demo.dll -- --check

# 5. 运行 --apply 应用更新并重启
dotnet run --project ./test-app/Moder.Update.Demo.dll -- --apply
```

### 示例程序命令

| 命令 | 描述 |
|------|------|
| `--version` | 显示当前版本 |
| `--check` | 使用本地目录检查更新 |
| `--apply` | 下载并应用更新包，然后重启 |
| `--create-package <from> <to> <source> <output>` | 创建测试更新包 |

### 创建更新包

```bash
# Linux/macOS
./scripts/create-demo-package.sh 1.0.0 1.1.0 ./my-app ./demo-packages

# Windows
scripts\create-demo-package.cmd 1.0.0 1.1.0 .\my-app .\demo-packages
```

### 示例程序包位置

示例程序从相对于二进制文件的 `../../../demo-packages` 目录查找更新包。该目录应包含：
- `catalog.json` — 更新目录
- `*.zst` — 更新包

## 许可协议

MIT
