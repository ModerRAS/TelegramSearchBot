# TelegramSearchBot 构建与测试指南

## 快速开始

### 环境要求
- **.NET SDK**: 10.0 或更高版本
- **操作系统**: Windows 10/11 (完整功能)、Linux (部分功能限制)、macOS (实验性)
- **数据库**: SQLite (内置支持)

### 构建命令

#### 1. 还原依赖
```bash
dotnet restore TelegramSearchBot.sln
```

#### 2. 构建项目
```bash
# 构建所有项目
dotnet build TelegramSearchBot.sln --configuration Release

# 构建特定项目
dotnet build TelegramSearchBot/TelegramSearchBot.csproj --configuration Release
```

#### 3. 发布应用

**Windows 平台**
```bash
# 发布 Windows 版本
dotnet publish TelegramSearchBot/TelegramSearchBot.csproj -c Release -r win-x64 --self-contained --output ./publish

# 发布 Windows 单文件版本
dotnet publish TelegramSearchBot/TelegramSearchBot.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true --output ./publish

# 生成 Moder.Update 兼容更新源
dotnet run --project TelegramSearchBot.UpdateBuilder/TelegramSearchBot.UpdateBuilder.csproj -c Release -- \
  --source-dir ./publish \
  --output-dir ./update-feed \
  --target-version 2026.04.23.2 \
  --min-source-version 2026.04.23.553
```

**Linux 平台**
```bash
# 发布 Linux 版本
dotnet publish TelegramSearchBot/TelegramSearchBot.csproj -c Release -r linux-x64 --self-contained
```

**macOS 平台**
```bash
# 发布 macOS 版本
dotnet publish TelegramSearchBot/TelegramSearchBot.csproj -c Release -r osx-x64 --self-contained
```

#### 4. Docker 构建
```bash
# 构建 Docker 镜像
docker build -t telegramsearchbot .

# 使用 Docker Compose
docker-compose up --build
```

### 测试命令

#### 1. 运行所有测试
```bash
dotnet test TelegramSearchBot.Test/TelegramSearchBot.Test.csproj
```

#### 2. 运行特定测试类别
```bash
# 运行向量索引相关测试
dotnet test --filter "Category=Vector"

# 运行 AI 服务测试
dotnet test --filter "FullyQualifiedName~AI"

# 运行集成测试
dotnet test --filter "FullyQualifiedName~Integration"
```

#### 3. 使用 PowerShell 脚本运行向量测试
```powershell
# Windows PowerShell
.\TelegramSearchBot.Test\RunVectorTests.ps1

# PowerShell Core (跨平台)
pwsh TelegramSearchBot.Test/RunVectorTests.ps1
```

#### 4. 详细测试输出
```bash
# 显示详细测试结果
dotnet test --verbosity normal --logger "console;verbosity=detailed"

# 生成测试报告
dotnet test --logger trx --results-directory TestResults
```

#### 5. 代码覆盖率
```bash
# 安装 coverlet 工具
dotnet tool install --global coverlet.console

# 运行带覆盖率的测试
coverlet TelegramSearchBot.Test/bin/Release/net10.0/TelegramSearchBot.Test.dll --target "dotnet" --targetargs "test --no-build"
```

### 开发环境设置

#### 1. 开发构建
```bash
# 开发模式构建
dotnet build --configuration Debug

# 监视模式 (自动重编译)
dotnet watch --project TelegramSearchBot run
```

#### 2. 数据库迁移
```bash
# 添加新的迁移
dotnet ef migrations add MigrationName --project TelegramSearchBot

# 更新数据库
dotnet ef database update --project TelegramSearchBot

# 生成 SQL 脚本
dotnet ef migrations script --project TelegramSearchBot
```

#### 3. 代码质量检查
```bash
# 使用 .editorconfig 格式化代码
dotnet format TelegramSearchBot.sln

# 检查代码样式
dotnet format --verify-no-changes TelegramSearchBot.sln
```

### 部署准备

#### 1. 生产环境构建
```bash
# 清理并重新构建
dotnet clean
dotnet build -c Release
dotnet test -c Release

# 发布最终版本
dotnet publish -c Release -r win-x64 --self-contained --output ./publish
```

#### 2. 环境配置
```bash
# 设置环境变量 (Linux/macOS)
export BOT_TOKEN="your_bot_token_here"
export DATABASE_PATH="./data/bot.db"

# 设置环境变量 (Windows PowerShell)
$env:BOT_TOKEN="your_bot_token_here"
$env:DATABASE_PATH="./data/bot.db"
```

### 故障排除

#### 常见构建问题

**问题**: 找不到 .NET 10.0 SDK
```bash
# 检查已安装的 SDK
dotnet --list-sdks

# 检查运行时
dotnet --list-runtimes
```

**问题**: Windows 特定依赖在 Linux 上构建失败
```bash
# 使用条件编译跳过 Windows 特定功能
dotnet build -c ReleaseLinux
```

**问题**: 测试数据库文件锁定
```bash
# 清理测试数据
rm -rf TelegramSearchBot.Test/bin/
rm -rf TelegramSearchBot.Test/obj/
```

#### 性能优化

**构建时间优化**
```bash
# 并行构建
dotnet build --parallel

# 增量构建
dotnet build --no-restore
```

**发布大小优化**
```bash
# 裁剪未使用的代码
dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true

# 使用 ReadyToRun 编译
dotnet publish -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true
```

### 持续集成

#### GitHub Actions 示例
```yaml
name: Build and Test
on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'
    - name: Restore dependencies
      run: dotnet restore
    - name: Build
      run: dotnet build --no-restore
    - name: Test
      run: dotnet test --no-build --verbosity normal
```

仓库内实际的 `push.yml` 现在维护两条发布线：
- 保留根目录中的最终 ClickOnce bridge，供旧安装版本过渡到新更新链路。
- 每次主分支发布都会生成 `catalog.json`、`packages/` 和 `moder_update_updater.exe`，供 `%LOCALAPPDATA%\TelegramSearchBot\app` 中的独立安装目录继续使用 Moder.Update 协议升级。
- `push.yml` 会把校验、`telegram-bot-api` 构建、Moder.Update updater 构建、独立包构建和桥接包构建尽量拆成并行 job，最后再统一上传到 Backblaze B2 与 GitHub Releases。
- 同一条发布流水线会在新文件上传成功后裁剪 Backblaze B2 上重复或过期的更新包版本，并把 `TelegramSearchBot-win-x64-full-<version>.zip` 全量包上传到 GitHub Releases，便于手动分发和回滚。

### 监控与日志

#### 本地调试日志
```bash
# 启用详细日志
dotnet run --project TelegramSearchBot --verbosity detailed

# 使用 Serilog 配置
export SERILOG__MINIMUMLEVEL__DEFAULT=Debug
```

#### 性能分析
```bash
# 使用 dotnet-trace
dotnet tool install --global dotnet-trace
dotnet trace collect --process-id <pid>

# 使用 dotnet-counters
dotnet tool install --global dotnet-counters
dotnet counters monitor --process-id <pid>
```

---

### 相关文档
- [Windows 依赖分析](./Windows_Dependencies_Analysis.md)
- [架构概览](./Architecture_Overview.md)
- [用户指南](./Bot_Commands_User_Guide.md)

*最后更新: 2026-04-23*
