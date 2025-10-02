# TelegramSearchBot Copilot Instructions

## 项目速览
- 解决方案以 `TelegramSearchBot` 主控制台为核心，配套 `TelegramSearchBot.Common`, `TelegramSearchBot.Search`, `TelegramSearchBot.Test/*` 等项目。
- 入口 `TelegramSearchBot/Program.cs` 负责 Serilog 日志、OpenTelemetry sink、HTTP2 设置，并通过 `GeneralBootstrap.Startup` 创建主机。
- 配置统一由 `TelegramSearchBot.Common/Env.cs` 从 `%LOCALAPPDATA%/TelegramSearchBot/Config.json` 加载；不要依赖 `appsettings.json`。

## 启动与依赖注入
- `AppBootstrap/AppBootstrap.cs` 会为 OCR/ASR/Scheduler 等模块派生进程（Windows JobObject 保证父进程退出即清理）。
- 默认会 `Fork(["Scheduler", port])` 启动局域 GarnetServer；Redis 客户端使用 `Env.SchedulerPort` 访问，开发调试勿硬编码端口。
- DI 通过 `Extension/ServiceCollectionExtension.ConfigureAllServices`：Scrutor 扫描 `IOnUpdate`/`IService`/`IView`，以及 `[Injectable]` 特性；新增服务时保持命名空间位于主项目使扫描生效。

## 消息处理管线
- `Service/BotAPI/TelegramBotReceiverService` 在 BackgroundService 中 `StartReceiving`，每条 Update 创建独立 Scope。
- `Executor/ControllerExecutor` 根据 `IOnUpdate.Dependencies` 拓扑排序执行；写新控制器需实现 `IOnUpdate`、维护 `List<Type>` 依赖，并通过 `PipelineContext.PipelineCache` 传递跨控制器数据。
- 控制器应无状态，所有持久化操作交由 `Service/*`（例如 `Service/Storage/MessageService`）。

## 搜索与数据
- EF Core 上下文 `Model/DataDbContext` 连接本地 SQLite `Env.WorkDir/Data.sqlite`，迁移脚本保存在 `Migrations/`。
- Lucene 搜索封装在 `Manager/LuceneManager`，语义检索由 `Service/Vector/FaissVectorService` + `TelegramSearchBot.Search` 管理，索引文件写入 `Env.WorkDir/faiss_indexes/`。
- 搜索命令控制器 (`Controller/Search/*`) 与服务层 (`Service/Search`) 紧耦；调整逻辑需同步更新分页缓存 `Service/Scheduler/SearchPageCacheCleanupTask`。

## 定时任务与 Garnet
- 所有计划任务实现 `IScheduledTask` 并使用 `[Injectable(ServiceLifetime.Singleton)]` 注册；Cron 表达式通过 `SchedulerService` 统一调度。
- `SchedulerService` 会在 SQLite 中记录执行状态与心跳，任务内部调用 `SetHeartbeatCallback` 避免被判定为僵死。
- 新任务若需要跨进程通信，请复用 `StackExchange.Redis` 连接（Garnet）实现分布式锁或队列。

## AI 与工具集成
- LLM 通道由 `Service/AI/LLM/*` 管理；`McpToolHelper.EnsureInitialized` 会扫描 `[McpTool]` 标记的方法生成工具 XML。
- 新增工具需放在同一程序集，配合 `[McpParameter]` 描述参数；`GeneralBootstrap` 启动时会自动注册并缓存 Prompt 片段。
- OCR/ASR/QR 相关控制器依赖独立进程服务（参见 `AppBootstrap/OCRBootstrap.cs` 等），调试时可单独以 `dotnet run --project TelegramSearchBot -- OCR` 启动。

## 构建与测试
- 常用命令：`dotnet restore TelegramSearchBot.sln`、`dotnet build TelegramSearchBot.sln -c Release`、`dotnet test`。
- 向量相关快速回归：`pwsh TelegramSearchBot.Test/RunVectorTests.ps1` 会筛选 `Category=Vector`。
- `dotnet watch run --project TelegramSearchBot` 可本地监听重载，但注意并行子进程仍会启动。

## 开发提示
- 所有持久化配置改动需更新 `Docs/README_VectorDatabaseInit.md` 或相关指南，保持用户文档同步。
- 在管线中共享对象使用 `PipelineContext.PipelineCache`，不要通过静态变量跨控制器传递引用。
- 长时间任务（OCR/下载）请放入 `Service/Scheduler` 或独立进程，避免在 `IOnUpdate` 同步阻塞。

## 常见陷阱
- 忽略 `%LOCALAPPDATA%/TelegramSearchBot` 目录会导致缺失配置/索引，调试前确保存在 `Config.json`。
- 新控制器忘记声明依赖将触发 `Circular dependency detected`；利用 `Dependencies` 显式排序，而不是尝试在代码中等待其他控制器。
- 修改向量结构后需要运行 `/faiss重建` 管理命令或调用相应服务刷新索引，否则搜索将落在旧文件上。
