__项目：TelegramSearchBot Orleans 重构方案__

__目标：__

将现有的 TelegramSearchBot 项目重构为基于 Microsoft Orleans Actor 模型的架构。主要目的是提升系统的可伸缩性、模块化程度、代码可维护性，并实现更灵活的消息处理流程，同时确保在重构过程中服务不中断，并支持多人并行开发。

__核心架构愿景：__

系统将围绕 Orleans Grains (Actors) 和 Orleans Streams 构建。外部消息（如来自 Telegram API 的消息）进入系统后，将被发布到特定的 Orleans Streams。不同的 Grains 将订阅这些 Streams，处理消息，并可能将处理结果或中间状态发布到其他 Streams，形成一个灵活的、事件驱动的处理流水线。最终的处理结果将通过专门的 Grain/服务发送回用户或进行存储。

__整体架构图：__

graph TD subgraph External\_Source \[Telegram API / Bot Client] TELEGRAM\_API\[Telegram API Ingress] end subgraph Application\_Core \[TelegramSearchBot - Orleans Based] %% Message Ingress & Dispatcher (Orleans Client) INGRESS\_DISPATCHER{Message Ingress & Dispatcher} %% Raw Message Streams (Initial Classification) STREAM\_RAW\_IMG\[Stream: RawImageMessages] STREAM\_RAW\_VID\[Stream: RawVideoMessages] STREAM\_RAW\_AUD\[Stream: RawAudioMessages] STREAM\_RAW\_TXT\[Stream: RawTextMessages] STREAM\_RAW\_CMD\[Stream: RawCommandMessages] STREAM\_RAW\_CBQ\[Stream: RawCallbackQueryMessages] %% Central Text Processing Stream STREAM\_TEXT\_CONTENT\[Stream: TextContentToProcess] %% Media Processing Grains GRAIN\_OCR\[OcrGrain] GRAIN\_QR\[QrCodeScanGrain] GRAIN\_ASR\[AsrGrain] %% Text & Command Processing Grains (Subscribers to STREAM\_TEXT\_CONTENT) GRAIN\_URL\_EXTRACT\[UrlExtractionGrain] GRAIN\_LLM\_PROC\[LlmProcessingGrain] GRAIN\_CMD\_PARSE\[CommandParsingGrain] GRAIN\_SEARCH\_QUERY\[SearchQueryGrain] GRAIN\_BILI\_LINK\_PROC\[BilibiliLinkProcessingGrain] GRAIN\_SHORTURL\_RESOLVE\[ShortUrlResolutionGrain] GRAIN\_OTHER\_TEXT\_LOGIC\[OtherTextLogicGrain] %% Callback Query Handler Grain GRAIN\_CBQ\_HANDLER\[CallbackQueryHandlerGrain] %% Output & Utility Grains/Services GRAIN\_TELEGRAM\_SENDER\[TelegramMessageSenderGrain] SERVICE\_MESSAGE\_STORAGE\[MessageStorageService (Persistent Storage)] %% Connections from Ingress to Raw Streams TELEGRAM\_API -- All Message Types --> INGRESS\_DISPATCHER INGRESS\_DISPATCHER -- Image --> STREAM\_RAW\_IMG INGRESS\_DISPATCHER -- Video --> STREAM\_RAW\_VID INGRESS\_DISPATCHER -- Audio --> STREAM\_RAW\_AUD INGRESS\_DISPATCHER -- Text --> STREAM\_RAW\_TXT INGRESS\_DISPATCHER -- Command --> STREAM\_RAW\_CMD INGRESS\_DISPATCHER -- CallbackQuery --> STREAM\_RAW\_CBQ %% Media Grains consuming Raw Streams and producing to Text Stream STREAM\_RAW\_IMG --> GRAIN\_OCR STREAM\_RAW\_IMG --> GRAIN\_QR STREAM\_RAW\_AUD --> GRAIN\_ASR STREAM\_RAW\_VID -- Extract Audio (if needed) --> GRAIN\_ASR GRAIN\_OCR -- OCRed Text --> STREAM\_TEXT\_CONTENT GRAIN\_QR -- QR Text --> STREAM\_TEXT\_CONTENT GRAIN\_ASR -- ASR Text --> STREAM\_TEXT\_CONTENT %% Raw Text/Command also feed into Text Stream STREAM\_RAW\_TXT -- Raw Text --> STREAM\_TEXT\_CONTENT STREAM\_RAW\_CMD -- Command Text --> STREAM\_TEXT\_CONTENT %% Text Processing Grains consuming from Central Text Stream STREAM\_TEXT\_CONTENT --> GRAIN\_URL\_EXTRACT STREAM\_TEXT\_CONTENT --> GRAIN\_LLM\_PROC STREAM\_TEXT\_CONTENT --> GRAIN\_CMD\_PARSE STREAM\_TEXT\_CONTENT --> GRAIN\_SEARCH\_QUERY STREAM\_TEXT\_CONTENT --> GRAIN\_BILI\_LINK\_PROC STREAM\_TEXT\_CONTENT --> GRAIN\_SHORTURL\_RESOLVE STREAM\_TEXT\_CONTENT --> GRAIN\_OTHER\_TEXT\_LOGIC %% Callback Query Processing STREAM\_RAW\_CBQ --> GRAIN\_CBQ\_HANDLER %% Grains sending responses via TelegramSenderGrain GRAIN\_OCR -- Result/Error --> GRAIN\_TELEGRAM\_SENDER GRAIN\_QR -- Result/Error --> GRAIN\_TELEGRAM\_SENDER GRAIN\_ASR -- Result/Error --> GRAIN\_TELEGRAM\_SENDER GRAIN\_URL\_EXTRACT -- Result/Error --> GRAIN\_TELEGRAM\_SENDER GRAIN\_LLM\_PROC -- Result/Error --> GRAIN\_TELEGRAM\_SENDER GRAIN\_CMD\_PARSE -- Result/Help --> GRAIN\_TELEGRAM\_SENDER GRAIN\_SEARCH\_QUERY -- Result --> GRAIN\_TELEGRAM\_SENDER GRAIN\_BILI\_LINK\_PROC -- Info/Error --> GRAIN\_TELEGRAM\_SENDER GRAIN\_SHORTURL\_RESOLVE -- URL/Error --> GRAIN\_TELEGRAM\_SENDER GRAIN\_CBQ\_HANDLER -- Result/Update --> GRAIN\_TELEGRAM\_SENDER %% Sending to external Telegram API GRAIN\_TELEGRAM\_SENDER -- Formatted Message --> TELEGRAM\_API %% Optional: Data Persistence GRAIN\_OCR -- Data to Store --> SERVICE\_MESSAGE\_STORAGE STREAM\_TEXT\_CONTENT -- Data to Store --> SERVICE\_MESSAGE\_STORAGE GRAIN\_SEARCH\_QUERY -- Search Log --> SERVICE\_MESSAGE\_STORAGE end

---

__重构阶段与任务分解：__

__Phase 0: 准备与基础架构 (负责人：架构组/核心开发者)__

- __Task 0.1: Grain 接口定义__

  - __描述__: 为所有规划中的 Grains 定义 C# 接口。这些接口是后续 Grain 开发的契约。
  - __产出__: 一个共享的 `.csproj` 项目，包含所有 Grain 接口定义 (e.g., `ITelegramSearchBot.Interfaces`)。
  - __示例接口__: `IOcrGrain`, `IQrCodeScanGrain`, `IUrlExtractionGrain`, `ILlmProcessingGrain`, `ICommandParsingGrain`, `IBilibiliLinkProcessingGrain`, `ICallbackQueryHandlerGrain`, `ITelegramMessageSenderGrain`。

- __Task 0.2: Orleans 项目集成与 Silo/Client 配置__

  - __描述__: 在解决方案中集成 Orleans NuGet 包。创建 Silo Host 项目，配置集群（开发阶段可使用 `UseLocalhostClustering()`）、序列化、服务发现等。在主应用程序中初始化 Orleans Client (`IClusterClient`)。
  - __产出__: 可运行的 Orleans Silo Host，主应用中可用的 `IClusterClient` 实例。

- __Task 0.3: Stream ID 和消息模型定义__

  - __描述__: 定义 Orleans Streams 的唯一标识符 (Stream IDs/GUIDs 和命名空间)。定义在 Stream 中流转的通用消息包装类 (e.g., `StreamMessage<T>`)，包含必要的元数据（原始消息ID, ChatID, UserID, 时间戳, 来源等）和具体内容 `T`。
  - __产出__: Stream ID 常量/配置类，`StreamMessage<T>` 定义。

- __Task 0.4: 消息入口适配器与功能开关机制__

  - __描述__: 在现有 Telegram 消息接收点（通常是处理 `Update` 对象的地方）实现一个适配器。此适配器将根据功能开关的配置，决定是将消息路由到旧的处理逻辑，还是将其包装成 `StreamMessage<T>` 并发布到相应的 Orleans Raw Stream。功能开关应支持按消息类型或特定功能模块进行配置。
  - __产出__: 消息分发适配器代码，功能开关读取和决策逻辑。

---

__Phase 1: 核心 Grain 实现 (可并行分配给不同开发者/小组)__

- __前提__: Phase 0 完成，特别是 Task 0.1 (接口定义) 和 Task 0.2 (基础架构)。

- __通用指南对每个 Grain 开发者__:

  - 获取分配的 Grain 接口定义。
  - 参考现有项目中对应的旧 Controller/Service 业务逻辑。
  - 实现 Grain 类，继承自 `Orleans.Grain` 并实现分配的接口。
  - 如果 Grain 需要消费 Stream，实现 Stream 订阅逻辑 (推荐使用 `[ImplicitStreamSubscription("YourStreamNamespace")]` 或在 `OnActivateAsync` 中手动订阅)。
  - 如果 Grain 处理结果需要进入下一流程，使用 `GetStreamProvider().GetStream().OnNextAsync()` 将结果发布到目标 Stream。
  - 如果 Grain 需要直接回复用户，通过注入/获取 `ITelegramMessageSenderGrain` 的引用并调用其方法。
  - 编写单元测试。

- __Task 1.1: `TelegramMessageSenderGrain` 实现__

  - __接口__: `ITelegramMessageSenderGrain`
  - __职责__: 接收来自其他 Grains 的发送请求，调用 Telegram Bot SDK 将消息发送给指定用户/聊天。

- __Task 1.2: `OcrGrain` 实现__

  - __接口__: `IOcrGrain`
  - __消费 Stream__: `RawImageMessages`
  - __职责__: 执行 OCR 处理。
  - __产生到 Stream__: `TextContentToProcess` (包含识别文本和元数据)。
  - __直接输出 (可选)__: 通过 `ITelegramMessageSenderGrain` 发送错误或处理状态。

- __Task 1.3: `QrCodeScanGrain` 实现__

  - __接口__: `IQrCodeScanGrain`
  - __消费 Stream__: `RawImageMessages`
  - __职责__: 执行二维码识别。
  - __产生到 Stream__: `TextContentToProcess` (包含识别文本和元数据)。

- __Task 1.4: `AsrGrain` 实现__

  - __接口__: `IAsrGrain`
  - __消费 Stream__: `RawAudioMessages` / `RawVideoMessages`
  - __职责__: 执行语音转文字。
  - __产生到 Stream__: `TextContentToProcess` (包含识别文本和元数据)。

- __Task 1.5: `UrlExtractionGrain` 实现__

  - __接口__: `IUrlExtractionGrain`
  - __消费 Stream__: `TextContentToProcess`
  - __职责__: 从文本中提取 URL，可能进行初步处理（如短链展开、获取标题）。
  - __输出__: 通过 `ITelegramMessageSenderGrain` 回复用户，或将更结构化的 URL 信息发布到特定 Stream (如果需要进一步处理)。

- __Task 1.6: `LlmProcessingGrain` 实现__

  - __接口__: `ILlmProcessingGrain`
  - __消费 Stream__: `TextContentToProcess` (可能根据特定触发条件，如命令或消息内容)。
  - __职责__: 调用大语言模型服务。
  - __输出__: 通过 `ITelegramMessageSenderGrain` 回复用户。

- __Task 1.7: `CommandParsingGrain` 实现__

  - __接口__: `ICommandParsingGrain`
  - __消费 Stream__: `TextContentToProcess` (或 `RawCommandMessages`)
  - __职责__: 解析用户命令和参数。根据命令分发给其他专门的命令处理 Grains (需定义相应接口) 或直接执行简单命令。
  - __输出__: 通过 `ITelegramMessageSenderGrain` 回复命令执行结果或帮助信息。

- __Task 1.8: `BilibiliLinkProcessingGrain` 实现__

  - __接口__: `IBilibiliLinkProcessingGrain`
  - __消费 Stream__: `TextContentToProcess`
  - __职责__: 检测文本中的 Bilibili 链接，调用 Bilibili 服务获取信息（可能通过另一个 `IBiliApiServiceGrain`）。
  - __输出__: 通过 `ITelegramMessageSenderGrain` 回复B站信息。

- __Task 1.9: `CallbackQueryHandlerGrain` 实现__

  - __接口__: `ICallbackQueryHandlerGrain`
  - __消费 Stream__: `RawCallbackQueryMessages`
  - __职责__: 处理来自 Telegram Inline Keyboard 的回调查询。它会解析回调数据，识别目标 Grain (例如 `SearchQueryGrain` 的特定实例) 和操作，然后调用该 Grain 的相应方法。
  - __输出__: 通过 `ITelegramMessageSenderGrain` 更新消息、发送新消息或通知（通常由被调用的目标 Grain 负责，此 Handler 主要做路由）。

- __Task 1.10: `SearchQueryGrain` 实现__
  - __接口__: `ISearchQueryGrain` (需在 Task 0.1 中定义)
  - __激活方式__: 通常由 `CommandParsingGrain` 在解析到搜索命令 (如 "搜索 <关键词>") 后，根据用户ID/聊天ID和搜索关键词组合激活或获取一个 `SearchQueryGrain` 实例。
  - __职责__:
    1.  接收搜索请求（包含查询关键词、初始分页参数等）。
    2.  调用 `ISearchService` (封装了 `LuceneManager` 的逻辑) 执行搜索。
    3.  **状态管理**: 在 Grain 内部持久化当前搜索会话的状态，包括原始查询、当前页码、每页数量、总结果数等。这将替代 `SearchNextPageController` 中使用的 LiteDB 缓存。
    4.  格式化搜索结果，并生成包含分页按钮（如"上一页"、"下一页"、"跳转页码"、"关闭搜索"）的 Telegram Inline Keyboard。按钮的 CallbackData 需要包含足够的信息以路由回此 Grain 的特定实例及相应的操作 (e.g., `searchgrain:<grainId>:next_page`, `searchgrain:<grainId>:page:3`, `searchgrain:<grainId>:cancel`)。
    5.  通过 `ITelegramMessageSenderGrain` 发送包含结果和分页按钮的消息。
    6.  实现处理分页回调的方法 (e.g., `HandleNextPageAsync()`, `HandlePreviousPageAsync()`, `HandleGoToPageAsync(int pageNumber)`, `HandleCancelSearchAsync()`)。这些方法会:
        *   由 `CallbackQueryHandlerGrain` 根据解析的 CallbackData 调用。
        *   更新 Grain 内部的搜索状态（如页码）。
        *   重新调用 `ISearchService` 获取新页面的数据。
        *   通过 `ITelegramMessageSenderGrain` 编辑原消息或发送新消息以展示新一页的结果和更新后的分页按钮。
        *   处理旧消息的删除（例如，编辑消息以移除旧按钮，或删除旧消息再发送新消息）。
  - __依赖__: `ISearchService`, `ITelegramMessageSenderGrain`.
  - __替换逻辑**: `SearchController.cs` (用于发起搜索) 和 `SearchNextPageController.cs` (用于处理分页回调和状态管理)。

- __Task 1.11: `FileDownloadGrain` 实现__
  - __接口__: `IFileDownloadGrain`
  - __消费 Stream__: `MediaDownloadRequests`
  - __职责__:（原有下载控制器逻辑迁移）

- __Task 1.12: `MessageStorageGrain` 实现__
  - __接口__: `IMessageStorageGrain`
  - __持久化__: 替换LiteDB存储方案

- __Task 1.13: `AuthGrain` 实现__
  - __接口__: `IAuthGrain`
  - __职责__:（整合权限校验逻辑）

- __Task 1.14: `ShortUrlGrain` 实现__
  - __接口__: `IShortUrlGrain`
  - __持久化__:（处理短链接数据库迁移）

---

__Phase 2: 集成、测试与逐步上线 (负责人：各 Grain 开发者 + 集成测试团队)__

- __描述__: 当一个或一组相关的 Grains 开发完成后，将其部署到 Orleans Silo。通过 Task 0.4 中实现的功能开关，在消息入口适配器中逐步将对应功能的流量切换到新的 Orleans 处理流程。

- __步骤__:

  1. __部署新 Grain(s)__ 到运行中的 Orleans Silo 集群。
  2. __配置功能开关__: 初始时，只对内部测试用户或极小比例的流量开启新流程。
  3. __监控与验证__: 密切监控新流程的日志、性能指标以及业务正确性。与旧流程的输出进行对比（如果可能）。
  4. __问题修复__: 如果发现问题，立即通过功能开关切回旧流程，修复问题后再尝试切换。__此机制保证主服务不中断。__
  5. __逐步扩大流量__: 确认稳定后，逐渐增加新流程的流量比例，直到100%。
  6. __重复此过程__ 直至所有规划的模块都迁移到 Orleans。

---

__Phase 3: 清理与优化 (负责人：架构组/核心开发者)__

- __Task 3.1: 移除旧代码__

  - __描述__: 当一个功能的 Orleans 实现已稳定运行并完全替代旧逻辑后，可以安全地从代码库中移除旧的 Controller/Service 实现。
  - __产出__: 更精简的代码库。

- __Task 3.2: 持久化配置 (生产环境)__

  - __描述__: 为 Grain 状态持久化和 Orleans Stream 持久化选择并配置适合生产环境的存储提供者 (e.g., Azure Storage, ADO.NET, Redis)。
  - __产出__: 生产环境的持久化配置。

- __Task 3.3: 性能优化与监控完善__

  - __描述__: 根据实际运行情况，对 Grains 和 Stream 进行性能分析和优化。完善 Orleans Dashboard 的使用，并集成更全面的监控告警方案 (e.g., Prometheus, Grafana, Application Insights)。
  - __产出__: 优化后的系统性能，完善的监控体系。

- __Task 3.4: 编写详尽的集成测试和端到端测试__

  - __描述__: 使用 Orleans TestKit 或 `TestCluster` 编写覆盖主要业务流程的集成测试。
  - __产出__: 健壮的自动化测试套件。

---

__并行协作与持续运行保障：__

- __接口先行__: 确保了各模块开发者可以基于共同的契约独立工作。
- __功能开关__: 允许新旧代码路径并存，按需切换，是保证服务不中断的核心。
- __模块化 Grain__: 每个 Grain 职责单一，降低了修改一个功能影响其他功能的风险。
- __Orleans 集群的独立性__: Silo 集群的部署和更新可以独立于主应用程序进行（如果主应用仅作为 Client）。
- __版本控制与分支策略__: 使用 Git 等版本控制系统，采用合适的分支策略（如 Gitflow 或基于特性的分支）来管理并行开发。

__给开发者的建议：__

- 熟悉 Orleans 的核心概念：Grains, Silo, Client, Streams, Grain生命周期, 异步编程 (`async/await`)。
- 优先保证接口的稳定性。如果接口需要变更，及时通知所有依赖方。
- 充分利用 Orleans 提供的日志和调试工具。
- 编写可测试的代码。

__补充章节：技术实现细节__

## 一、多进程架构迁移策略

### 1. 现状分析
目前系统使用 AppBootstrap 机制启动独立进程来处理可能存在内存泄漏的服务（OCR、ASR等）。主进程通过进程间通信分发任务，接收结果后独立进程退出，以此规避内存泄漏。

### 2. Orleans迁移方案

#### 方案A：完全迁移到Orleans Grain（推荐）

```csharp
// 示例：OCR Grain的生命周期管理
public class OcrGrain : Grain, IOcrGrain
{
    private PaddleOCR _ocrEngine;
    private DateTime _lastUsed;
    
    public override async Task OnActivateAsync(CancellationToken ct)
    {
        _ocrEngine = new PaddleOCR();
        _lastUsed = DateTime.UtcNow;
        RegisterTimer(CheckIdleTimeout, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private async Task CheckIdleTimeout(object state)
    {
        if (DateTime.UtcNow - _lastUsed > TimeSpan.FromMinutes(10))
        {
            await DeactivateOnIdle();
        }
    }
    
    public async Task<string> ProcessImageAsync(byte[] imageData)
    {
        _lastUsed = DateTime.UtcNow;
        return await _ocrEngine.ProcessAsync(imageData);
    }
}
```

#### 方案B：Orleans管理的多进程架构

```csharp
public class ProcessManagerGrain : Grain, IProcessManagerGrain
{
    private Process _currentProcess;
    private IDisposable _healthCheckTimer;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        _healthCheckTimer = RegisterTimer(
            CheckProcessHealth,
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    private async Task CheckProcessHealth(object state)
    {
        if (_currentProcess?.HasExited ?? false)
        {
            await RestartProcessAsync();
        }
    }
}
```

## 二、状态机迁移策略

### 1. 基于Grain的状态机实现

```csharp
// 状态机基础接口
public interface IStateMachineGrain : IGrainWithStringKey
{
    Task<StateResponse> TransitAsync(StateInput input);
    Task<CurrentState> GetCurrentStateAsync();
    Task ResetAsync();
}

// LLM配置编辑状态机示例
public class LlmConfigStateMachineGrain : Grain<LlmConfigState>, IStateMachineGrain
{
    private readonly Dictionary<string, Func<StateInput, Task<StateResponse>>> _stateHandlers;
    
    public LlmConfigStateMachineGrain()
    {
        _stateHandlers = new Dictionary<string, Func<StateInput, Task<StateResponse>>>
        {
            ["Initial"] = HandleInitialState,
            ["AwaitingChannelName"] = HandleChannelNameInput,
            ["AwaitingEndpoint"] = HandleEndpointInput,
            // ... 其他状态处理器
        };
    }
}
```

## 三、搜索功能优化

### 1. 分布式搜索架构

```csharp
public interface ISearchCoordinatorGrain : IGrainWithStringKey
{
    Task<SearchResults> SearchAsync(SearchQuery query);
    Task<SearchResults> GetNextPageAsync(string searchId, int page);
}

public class SearchCoordinatorGrain : Grain<SearchCoordinatorState>, ISearchCoordinatorGrain
{
    private readonly List<ISearchIndexGrain> _indexShards;
    private readonly ISearchResultCache _cache;

    public async Task<SearchResults> SearchAsync(SearchQuery query)
    {
        // 1. 检查缓存
        var cachedResult = await _cache.GetAsync(query.GetCacheKey());
        if (cachedResult != null) return cachedResult;

        // 2. 并行查询所有分片
        var tasks = _indexShards.Select(shard => shard.SearchAsync(query));
        var results = await Task.WhenAll(tasks);

        // 3. 合并结果
        var mergedResults = MergeResults(results);
        
        // 4. 缓存结果
        await _cache.SetAsync(query.GetCacheKey(), mergedResults);
        
        return mergedResults;
    }
}
```

## 四、消息处理流水线

### 1. 消息分类与路由

```csharp
public interface IMessageClassifierGrain : IGrainWithGuidKey
{
    Task ClassifyAndRouteAsync(Message message);
}

public class MessageClassifierGrain : Grain, IMessageClassifierGrain
{
    private readonly IStreamProvider _streamProvider;

    public async Task ClassifyAndRouteAsync(Message message)
    {
        var streamMessage = new StreamMessage<Message>
        {
            Content = message,
            Metadata = new MessageMetadata
            {
                MessageId = message.Id,
                ChatId = message.ChatId,
                Timestamp = DateTime.UtcNow
            }
        };

        var stream = GetAppropriateStream(message);
        await stream.OnNextAsync(streamMessage);
    }
}
```

### 2. 错误处理与重试

```csharp
public class RetryableGrain : Grain
{
    protected async Task ExecuteWithRetryAsync(Func<Task> action, int maxRetries = 3)
    {
        var retryCount = 0;
        while (true)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (retryCount < maxRetries)
            {
                retryCount++;
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
            }
        }
    }
}
```

## 五、数据迁移策略

### 1. LiteDB到Orleans存储的迁移

```csharp
public interface IDataMigrationGrain : IGrainWithGuidKey
{
    Task StartMigrationAsync();
    Task<MigrationStatus> GetStatusAsync();
}

public class DataMigrationGrain : Grain<MigrationState>, IDataMigrationGrain
{
    public async Task StartMigrationAsync()
    {
        // 1. 创建快照
        await CreateSnapshot();

        // 2. 增量同步
        var lastSyncPoint = await GetLastSyncPoint();
        await SyncIncrementalChanges(lastSyncPoint);

        // 3. 验证
        await ValidateMigration();
    }
}
```

## 六、监控和可观测性

### 1. 自定义指标收集

```csharp
public class MonitoredGrain : Grain
{
    private readonly ITelemetryClient _telemetry;

    protected async Task TrackOperation(string operation, Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await action();
            _telemetry.TrackMetric($"Grain.{operation}.Duration", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _telemetry.TrackException(ex);
            throw;
        }
    }
}
```

### 2. 健康检查

```csharp
public interface IHealthCheckGrain : IGrainWithGuidKey
{
    Task<HealthStatus> CheckHealthAsync();
}

public class HealthCheckGrain : Grain, IHealthCheckGrain
{
    public async Task<HealthStatus> CheckHealthAsync()
    {
        var checks = new[]
        {
            CheckSiloHealth(),
            CheckStorageHealth(),
            CheckStreamProviderHealth()
        };

        var results = await Task.WhenAll(checks);
        return AggregateResults(results);
    }
}
```

## 七、权限管理

### 1. 基于Grain的权限系统

```csharp
public interface IAuthorizationGrain : IGrainWithStringKey
{
    Task<bool> CheckPermissionAsync(string userId, string permission);
    Task GrantPermissionAsync(string userId, string permission);
    Task RevokePermissionAsync(string userId, string permission);
}

public class AuthorizationGrain : Grain<AuthorizationState>, IAuthorizationGrain
{
    private readonly IPermissionCache _cache;

    public async Task<bool> CheckPermissionAsync(string userId, string permission)
    {
        // 1. 检查缓存
        if (await _cache.TryGetPermissionAsync(userId, permission, out var hasPermission))
        {
            return hasPermission;
        }

        // 2. 检查持久化状态
        hasPermission = State.Permissions.Contains((userId, permission));
        
        // 3. 更新缓存
        await _cache.SetPermissionAsync(userId, permission, hasPermission);
        
        return hasPermission;
    }
}
```

## 八、部署策略

### 1. 容器化配置

```dockerfile
# Silo Host Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["TelegramSearchBot.Silo/TelegramSearchBot.Silo.csproj", "TelegramSearchBot.Silo/"]
RUN dotnet restore "TelegramSearchBot.Silo/TelegramSearchBot.Silo.csproj"
COPY . .
WORKDIR "/src/TelegramSearchBot.Silo"
RUN dotnet build "TelegramSearchBot.Silo.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TelegramSearchBot.Silo.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TelegramSearchBot.Silo.dll"]
```

### 2. Kubernetes配置

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: telegram-searchbot-silo
spec:
  replicas: 3
  selector:
    matchLabels:
      app: telegram-searchbot-silo
  template:
    metadata:
      labels:
        app: telegram-searchbot-silo
    spec:
      containers:
      - name: silo
        image: telegram-searchbot-silo:latest
        env:
        - name: ORLEANS_SILO_PORT
          value: "11111"
        - name: ORLEANS_GATEWAY_PORT
          value: "30000"
```

## 九、测试策略

### 1. Grain单元测试

```csharp
public class SearchQueryGrainTests
{
    private TestCluster _cluster;
    private ISearchQueryGrain _grain;

    [SetUp]
    public async Task Setup()
    {
        _cluster = new TestClusterBuilder()
            .AddSiloBuilderConfigurator<TestSiloConfigurator>()
            .Build();
        await _cluster.DeployAsync();
        _grain = _cluster.GrainFactory.GetGrain<ISearchQueryGrain>("test");
    }

    [Test]
    public async Task Search_WithValidQuery_ReturnsResults()
    {
        // Arrange
        var query = new SearchQuery { Keyword = "test" };

        // Act
        var result = await _grain.SearchAsync(query);

        // Assert
        Assert.That(result.Items, Is.Not.Empty);
    }
}
```

### 2. 集成测试

```csharp
public class MessageProcessingTests
{
    private IClusterClient _client;
    private IStreamProvider _streamProvider;

    [SetUp]
    public async Task Setup()
    {
        var builder = new ClientBuilder()
            .UseLocalhostClustering()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IStreamProvider, TestStreamProvider>();
            });

        _client = builder.Build();
        await _client.Connect();
    }

    [Test]
    public async Task ProcessMessage_CompleteFlow_SuccessfullyProcessed()
    {
        // Arrange
        var message = new Message { /* ... */ };
        var stream = _streamProvider.GetStream<Message>("test-stream");

        // Act
        await stream.OnNextAsync(message);

        // Assert
        // Verify message was processed through the entire pipeline
    }
}
```

## 十、性能优化建议

1. Grain激活优化
   - 使用粘性激活
   - 实现智能预热
   - 合理设置空闲超时

2. 流处理优化
   - 使用批处理
   - 实现背压机制
   - 合理分片

3. 状态持久化优化
   - 使用写入缓冲
   - 实现增量存储
   - 选择合适的存储提供程序

## 十一、迁移顺序建议

1. 第一阶段（基础设施）：
   - 搭建Orleans集群
   - 实现消息入口适配器
   - 部署监控系统

2. 第二阶段（核心功能）：
   - 迁移搜索功能
   - 迁移消息存储
   - 迁移权限系统

3. 第三阶段（AI服务）：
   - 迁移OCR服务
   - 迁移ASR服务
   - 迁移LLM服务

4. 第四阶段（辅助功能）：
   - 迁移URL处理
   - 迁移B站集成
   - 迁移文件下载

5. 第五阶段（优化完善）：
   - 性能优化
   - 监控完善
   - 文档更新

每个阶段都应该包含完整的测试覆盖，并且在迁移过程中保持系统的可用性。建议采用灰度发布策略，逐步将流量迁移到新系统。
