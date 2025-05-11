# TelegramSearchBot - 现有代码库概览

本文档旨在对现有的 `TelegramSearchBot` 项目结构及其关键组件进行高级概述，旨在帮助开发人员在准备进行 Orleans 重构时熟悉代码库。

## 根目录文件

*   **`Program.cs`**: 应用程序的主要入口点。处理应用程序启动、依赖注入 (DI) 容器设置、配置加载以及启动 Telegram 机器人客户端。
    *   *与 Orleans 的相关性*: Orleans Silo 主机初始化和 `IClusterClient` 设置可能会在此处或类似的启动类中集成。现有的 DI 注册将需要审查，并可能需要为 Grains 进行调整。
*   **`Env.cs`**: 可能包含特定于环境的配置或常量。
    *   *与 Orleans 的相关性*: Orleans 配置也可能利用或补充此文件。
*   **`Utils.cs`**: 包含整个应用程序中使用的各种实用方法。
    *   *与 Orleans 的相关性*: Grains 可能会使用实用方法，或者这些方法可能被重构为共享服务。
*   **`appsettings.json`**: 应用程序的配置文件（例如，连接字符串、API 密钥、机器人令牌）。(用户反馈：此项目实际未使用此文件进行配置)
*   **`TelegramSearchBot.csproj`**: C# 项目文件，定义依赖项、构建设置等。

## 主要目录及其内容

### 1. `AppBootstrap/`

此目录包含在启动期间负责引导应用程序各个部分的类。**核心机制：这些引导程序用于启动独立进程来处理可能存在内存泄漏的服务（如 OCR、ASR）。主服务通过进程间通信将任务分发给这些独立进程，待处理完成后接收结果，独立进程随后退出，以此方式规避内存泄漏问题。**

*   **`AppBootstrap.cs`**: 一个通用或主引导类，可能会调用其他特定的引导程序，协调多进程服务的启动。
*   **`ASRBootstrap.cs`**: 设置与自动语音识别 (ASR) 相关的独立进程服务。相关的 Service/Controller 会通过此引导程序建立的通信机制与 ASR 进程交互。
*   **`DaemonBootstrap.cs`**: 设置后台服务或守护进程。
*   **`GeneralBootstrap.cs`**: 设置通用或常用服务。
*   **`OCRBootstrap.cs`**: 设置与光学字符识别 (OCR) 相关的独立进程服务（例如，使用 `PaddleOCR.cs`）。相关的 Service/Controller 会通过此引导程序建立的通信机制与 OCR 进程交互。
*   **`QRBootstrap.cs`**: 设置与二维码处理相关的独立进程服务。
*   **`SchedulerBootstrap.cs`**: 设置计划任务或作业（用户反馈：`JobManager` 已未使用）。
    *   *与 Orleans 的相关性*: 这种多进程架构旨在解决内存泄漏，Orleans 通过 Grains 的激活/停用和独立的执行上下文可能提供更内聚的解决方案。现有服务注册将需要审查。某些服务可能会被 Grains 替换，而其他服务可能会被注入到 Grains 中。计划任务可能会被重构为基于 Reminder 或 Timer 的 Grains。

### 2. `Attributes/`

包含用于声明式编程或元数据的自定义 C# 特性。

*   **`BotCommandAttribute.cs`**: **此特性专门用于向 Telegram注册机器人命令及其描述，本身不直接影响程序的核心业务逻辑执行流程，主要负责命令的声明和元数据提供。**
    *   *与 Orleans 的相关性*: 命令处理将被重构。此特性声明的命令信息可用于配置 `CommandParsingGrain` 或特定的命令 Grains。
*   **`McpAttributes.cs`**: 与模型上下文协议 (MCP) 相关的特性（如果使用）。

### 3. `Comparer/`

*   **`MessageComparer.cs`**: 为 `Message` 对象实现自定义比较逻辑，可能用于排序或去重操作。

### 4. `Controller/`

这是当前应用程序的主要部分，可能遵循类似 MVC 的模式或命令处理程序模式，其中控制器接收输入（例如，Telegram 更新、命令）并编排业务逻辑。**这些控制器中的大部分逻辑都是迁移到 Orleans Grains 的主要候选者。**

*   **`Controller/AI/`**: 包含用于各种 AI 功能的控制器。
    *   `ASR/AutoASRController.cs`: 处理 ASR 请求，可能通过 `ASRBootstrap` 启动的外部进程。
    *   `LLM/GeneralLLMController.cs`: 处理与大型语言模型的交互。
    *   `OCR/AutoOCRController.cs`: 处理 OCR 请求，可能通过 `OCRBootstrap` 启动的外部进程。
    *   `QR/AutoQRController.cs`: 处理二维码扫描请求，可能通过 `QRBootstrap` 启动的外部进程。
    *   *与 Orleans 的相关性*: 这些控制器的逻辑可能会分别迁移到 `AsrGrain`、`LlmProcessingGrain`、`OcrGrain` 和 `QrCodeScanGrain`。Orleans Grains 可以直接包含这些处理逻辑，或与外部进程通信（如果仍需隔离）。

*   **`Controller/Bilibili/`**
    *   `BiliMessageController.cs`: 处理与 Bilibili 相关的命令或消息（例如，获取视频信息、下载）。
    *   *与 Orleans 的相关性*: 逻辑将移至 `BilibiliLinkProcessingGrain` 和其他潜在的 Bilibili 特定 Grains。

*   **`Controller/Download/`**
    *   `DownloadAudioController.cs`, `DownloadPhotoController.cs`, `DownloadVideoController.cs`: 处理下载不同媒体类型的请求。这些控制器可能也包含了从 Telegram 下载文件的逻辑，对应于 `IProcessAudio/Photo/Video` 接口。
    *   *与 Orleans 的相关性*: 下载任务可以由 Grains 管理，特别是如果它们是长时间运行或需要状态管理的（例如，`DownloadTaskGrain`）。

*   **`Controller/Manage/`**
    *   `AdminController.cs`: 处理管理命令。
    *   `CheckBanGroupController.cs`: 用于检查被封禁群组的逻辑。
    *   `EditLLMConfController.cs`: 用于编辑 LLM 配置。
    *   `RefreshController.cs`: 用于与刷新相关的命令。
    *   *与 Orleans 的相关性*: 管理功能可能由特定的管理 Grains 处理，或通过导致管理 Grains 的命令解析机制处理。

*   **`Controller/Search/`**
    *   `SearchController.cs`: 处理搜索查询。
    *   `SearchNextPageController.cs`: 处理搜索结果的分页。 (此文件在 `list_files` 中未直接列出，但根据模式推断存在)
    *   *与 Orleans 的相关性*: 搜索逻辑可以封装在 `SearchQueryGrain` 中。

*   **`Controller/Storage/`**
    *   `MessageController.cs`: 与消息存储交互，可能用于消息的 CRUD 操作。
    *   *与 Orleans 的相关性*: 与存储的直接交互可能由 Grains 调用的 `MessageStorageService` 处理，或者特定的 Grains 可能管理与消息相关的自身状态。

### 5. `Exceptions/`

特定错误条件的自定义异常类型。

*   `CannotGetAudioException.cs`, `CannotGetPhotoException.cs`, `CannotGetVideoException.cs`.

### 6. `Executor/`

*   **`ControllerExecutor.cs`**: **此类负责调度实现了 `IOnUpdate` 接口的“控制器”或处理程序的执行顺序。它接收一组 `IOnUpdate` 实例，并根据它们之间声明的依赖关系（通过 `Dependencies` 属性）来决定执行顺序。它会先执行没有未满足依赖项的控制器，然后逐步执行其他控制器，直到所有控制器都执行完毕。如果检测到循环依赖或无法满足的依赖，则会抛出异常。**
    *   *与 Orleans 的相关性*: Orleans Streams 和 Grains 的订阅机制天然支持并行和顺序处理，可以替代这种自定义的执行器逻辑。消息流可以配置为扇出到多个并行处理的 Grains，或者通过一系列 Grains 进行顺序处理。

### 7. `Extension/`

*   **`IDatabaseAsyncExtension.cs`**: 可能包含数据库操作的扩展方法，可能用于 `IQueryable` 或 `DbContext`。

### 8. `Handler/`

负责处理特定事件或通知的类。

*   **`UrlProcessingNotificationHandler.cs`**: **此类实现了 `INotificationHandler<TextMessageReceivedNotification>` (可能使用 MediatR)。它的核心功能是处理接收到的文本消息：**
    1.  **静默URL处理与存储**: 对每条消息中的URL进行异步处理（通过 `UrlProcessingService`），尝试将短链接或重定向链接展开为其最终的目标长链接。如果原始URL与处理后的URL不同且处理后的URL有效，则将此映射关系（原始URL -> 目标URL）存储到数据库的 `ShortUrlMappings` 表中。此过程会进行去重，并避免覆盖数据库中已有的有效映射。
    2.  **`/resolveurls` 命令处理**: 响应用户明确发出的 `/resolveurls` 命令。此命令可以附加文本参数，或回复给一条包含链接的消息。处理器会提取目标文本中的URL，再次调用 `UrlProcessingService` 进行实时处理，并优先从数据库中查找已存储的有效长链接进行回复。如果数据库中没有或存储的无效，则使用实时处理的结果。
    *   *与 Orleans 的相关性*: 这种事件驱动的逻辑与 Orleans Streams 非常吻合。URL的静默处理和命令处理都可以迁移到专门的 `UrlExtractionGrain` 或 `UrlResolutionGrain`。该 Grain 可以订阅包含文本消息的 Stream，并将处理结果（如解析后的长链接）发送给用户或存储。`UrlProcessingService` 的逻辑可以被此 Grain 直接调用或部分整合。

### 9. `Intrerface/` (拼写错误, 可能是 "Interface")

定义服务和其他组件的 C# 接口。

*   `ILLMService.cs`: LLM 服务的接口。(用户反馈：本质上未使用)
*   `IMessageService.cs`: 消息存储/检索服务的接口。(用户反馈：未使用，但其实现类 `MessageService.cs` 被使用)
*   `IOnCallbackQuery.cs`, `IOnUpdate.cs`: Telegram 更新处理不同阶段的接口。`IOnUpdate` 被 `ControllerExecutor` 用于调度。
*   `IPreUpdate.cs`: Telegram 更新处理的接口。(用户反馈：已过时)
*   `IProcessAudio.cs`, `IProcessPhoto.cs`, `IProcessVideo.cs`: 媒体处理的接口。**这些接口的实现可能包含从 Telegram 下载文件的逻辑。**
*   `ISearchService.cs`: 搜索服务的接口。
*   `IService.cs`: 通用服务标记接口或基础接口。
*   `IStreamService.cs`: 处理数据流的服务的接口。(用户反馈：未使用)
*   `ITokenManager.cs`: 令牌管理的接口。(用户反馈：未使用)
    *   *与 Orleans 的相关性*: Orleans 重构将涉及创建新的 Grain 接口。部分现有接口（如媒体处理）的逻辑将被纳入 Grains。

### 10. `Manager/`

包含管理核心业务逻辑或与外部工具/库交互的类。

*   **`JobManager.cs`**: 管理计划作业或后台任务。(用户反馈：未使用，写了一半)
*   **`LiteDbManager.cs`**: 管理与 LiteDB 数据库的交互。(用户反馈：未使用)
*   **`LuceneManager.cs`**: 管理与 Lucene.Net 搜索引擎库的交互。
*   **`PaddleOCR.cs`**: PaddleOCR 库的包装器或管理器。**此模块提供实际的 OCR 处理能力，并被 `OCRBootstrap` 用于创建独立的 OCR 处理进程。**
*   **`QRManager.cs`**: 管理二维码生成或扫描。
*   **`SendMessage.cs`**: 用于通过 Telegram 发送消息的实用程序或管理器类。
    *   *与 Orleans 的相关性*: 此功能将集中在 `TelegramMessageSenderGrain` 中。
*   **`WhisperManager.cs`**: Whisper ASR 模型的包装器或管理器。

### 11. `MCP/WebScraper/`

包含与 Web Scraper 相关的组件。(用户反馈：完全未使用的文件夹)

### 12. `Migrations/`

包含 Entity Framework Core 数据库迁移文件。这些文件定义了数据库模式的演变。

### 13. `Model/`

定义数据结构、POCO (Plain Old CLR Objects)、DTO (Data Transfer Objects) 和数据库上下文。

*   **`DataDbContext.cs`**: Entity Framework Core 的 DbContext 类，代表与数据库的会话，用于查询和保存数据。是数据持久化的核心。
*   **`DataDbContextFactory.cs`**: 用于在设计时（例如执行迁移命令时）或特定依赖注入场景中创建 `DataDbContext` 实例的工厂类。
*   **`ExportModel.cs`**: 可能用于定义数据导出操作时的数据结构或参数。(用户反馈：历史遗留，代码中存在 `[Obsolete]` 标记，可能已废弃/可删除)
*   **`ImportModel.cs`**: 可能用于定义数据导入操作时的数据结构或参数。(用户反馈：历史遗留，代码中存在 `[Obsolete]` 标记，可能已废弃/可删除)
*   **`MessageOption.cs`**: 存储与消息处理相关的选项或配置，例如特定消息类型的处理方式。
*   **`SearchOption.cs`**: 定义搜索操作的参数和选项，如搜索关键词、过滤条件、分页信息等。
*   **`SendModel.cs`**: 用于封装发送消息时所需的数据，如接收者ID、消息内容、格式选项等。
*   **`TokenModel.cs`**: 可能用于表示认证令牌或API访问令牌的数据结构。(用户反馈 `ITokenManager` 未使用，此模型可能也未使用或用途有限)。

*   **`Model/AI/`**
    *   **`LLMProvider.cs`**: 可能是一个枚举或常量类，用于定义支持的LLM提供商类型（如 OpenAI, Ollama）。

*   **`Model/Bilibili/`**: 包含与Bilibili服务交互时使用的数据模型。
    *   **`BiliOpusInfo.cs`**: 表示Bilibili动态（Opus）的信息，如作者、内容、图片、发布时间等。
    *   **`BiliVideoInfo.cs`**: 表示Bilibili视频的信息，如标题、作者、封面、播放链接、时长等。

*   **`Model/CloudPaste/`**: 包含与云剪贴板功能交互时使用的数据模型。(用户反馈：未完成的功能，当前未使用)
    *   **`CloudPasteLoginRequest.cs`**: 发送登录到云剪贴板服务的请求模型。
    *   **`CloudPasteLoginResponse.cs`**:接收云剪贴板服务登录响应的模型。
    *   **`CloudPastePostRequest.cs`**: 发送内容到云剪贴板服务的请求模型。
    *   **`CloudPastePostResult.cs`**: 接收云剪贴板服务发布结果的模型。

*   **`Model/Data/`**: 包含核心的、通常映射到数据库表的实体类。
    *   **`AppConfigurationItem.cs`**: 表示应用程序配置项的实体，可能用于存储动态配置。
    *   **`CacheData.cs`**: 通用的缓存数据模型，可能用于存储各种需要缓存的信息。
    *   **`ChannelWithModel.cs`**: 表示LLM渠道（Channel）及其关联模型（Model）的配置。
    *   **`GroupData.cs`**: 表示Telegram群组相关数据的实体。
    *   **`GroupSettings.cs`**: 存储特定Telegram群组的设置或配置。
    *   **`LLMChannel.cs`**: 表示一个LLM渠道（例如特定的API端点或配置集）的实体。
    *   **`Message.cs`**: 核心实体，表示一条Telegram消息及其相关信息（如消息ID, 发送者, 内容, 时间等）。
    *   **`ShortUrlMapping.cs`**: 用于存储短链接与其原始长链接之间映射关系的实体。由 `UrlProcessingNotificationHandler` 使用。
    *   **`TelegramFileCacheEntry.cs`**: 表示Telegram文件缓存条目的实体，用于缓存已下载或处理过的文件信息（如File ID, 本地路径）。
    *   **`UserData.cs`**: 表示Telegram用户相关数据的实体。
    *   **`UserWithGroup.cs`**: 可能表示用户与群组之间关联关系的实体或视图模型。

*   **`Model/Notifications/`**: 包含用于应用程序内部通知或事件的数据模型。
    *   **`TextMessageReceivedNotification.cs`**: 当接收到文本消息时，用于创建通知对象的模型，供事件处理器（如 `UrlProcessingNotificationHandler`）使用。

    *   *与 Orleans 的相关性*: 这些数据模型在重构后大部分会保持不变，因为它们代表了业务领域的核心数据。Grains 将使用这些模型来表示其状态或在消息中传递它们。`StreamMessage<T>` (在Orleans方案中定义) 将用于包装这些模型，以便在Orleans Streams中携带元数据进行传递。

### 14. `Properties/`

*   **`launchSettings.json`**, **`PublishProfiles/`**: (与核心业务逻辑和重构关联不大)

### 15. `Resources/`

静态资源。**据用户反馈，此目录包含二维码识别相关的模型，重构时这些资源可能不需要移动。**

### 16. `Service/`

包含封装业务逻辑的服务类。

*   **`Service/Abstract/`**: 服务的基类或抽象。
*   **`Service/AI/`**: 与 AI 功能相关的服务。
    *   `LLM/`: `BaseLlmService.cs` (用户反馈 `ILLMService` 未使用，搜索显示此基类未被直接子类化使用，可能已废弃), `GeneralLLMService.cs`, `McpToolHelper.cs`, `OllamaService.cs`, `OpenAIService.cs`。
    *   *与 Orleans 的相关性*: LLM 相关逻辑将由 `LlmProcessingGrain` 调用或整合。
*   **`Service/Bilibili/`**: 用于 Bilibili 集成的服务。
    *   `BiliApiService.cs`, `DownloadService.cs`, `TelegramFileCacheService.cs`, `IBiliApiService.cs`, `IDownloadService.cs`, `ITelegramFileCacheService.cs`。
    *   *与 Orleans 的相关性*: 逻辑将由与 Bilibili 相关的 Grains 调用或移入其中。
*   **`Service/BotAPI/`**: 用于与 Telegram Bot API 交互的服务。
    *   `BotCommandService.cs`: 管理机器人命令注册和分发。
    *   `SendMessageService.cs`: 专门用于发送消息的服务。
    *   *与 Orleans 的相关性*: `BotCommandService` 逻辑可能是 `CommandParsingGrain` 的一部分。`SendMessageService` 功能将由 `TelegramMessageSenderGrain` 处理。
*   **`Service/Common/`**: 通用或共享服务。
    *   `AppConfigurationService.cs`: 用于访问应用程序配置的服务。
    *   `UrlProcessingService.cs`: 被 `UrlProcessingNotificationHandler` 用来实际处理URL的展开等逻辑。
    *   `FeatureToggleService.cs` (如果存在或计划用于重构): 管理功能开关。
    *   *与 Orleans 的相关性*: 如果需要，这些可能会作为依赖项注入到 Grains 中，或者它们的逻辑可能会被像 `UrlExtractionGrain` 这样的 Grains 吸收。
*   **`Service/Manage/`**: 用于管理或行政任务的服务。
    *   `AdminService.cs`。
*   **`Service/Search/`**: 与搜索功能相关的服务。
    *   可能包含 `ISearchService` 的实现。
*   **`Service/StateMachine/`**: 与状态机实现相关的服务。(用户反馈：此目录原用于EditLLMConfService的状态机，但未完成且已被删除)
*   **`Service/Storage/`**: 用于数据持久性的服务。
    *   `MessageService.cs`: **实际的 `MessageService.cs` 类被用于消息的CRUD操作，但其对应的接口 `IMessageService.cs` (在 `Intrerface/` 目录下) 用户反馈并未使用。**
    *   *与 Orleans 的相关性*: 此服务可能会保留并注入到需要持久化或查询消息数据的 Grains 中，或者 Grains 可能使用 Orleans 持久性提供程序直接管理自己的状态。
*   **`Service/Tools/`**: **此目录包含作为工具注入到LLM中的服务。这些服务通过特定注解被 `Service/AI/LLM/McpToolHelper.cs` 发现并集成到LLM的工具调用流程中。**
    *   `DuckDuckGoToolService.cs`, `ShortUrlToolService.cs`。

### 17. `Sink/`

*   **`SharedMemoryLogReceiverSink.cs`, `SharedMemoryLogSink.cs`**: 自定义 Serilog sinks。(搜索显示未在项目其他地方引用，用户怀疑未使用，可能已废弃)

## 结论

此概述应提供对 `TelegramSearchBot` 当前架构的基本了解。在 Orleans 重构期间，开发人员将需要：

1.  识别控制器、服务和管理器中的特定逻辑片段，特别是 `AppBootstrap` 如何协调外部进程以处理潜在内存泄漏的服务。
2.  为这些逻辑片段设计相应的 Grain 接口和实现，考虑 Orleans 是否能通过其自身的生命周期管理和激活机制来替代外部进程方案。
3.  确定这些新 Grains 之间的数据流方式，主要使用 Orleans Streams。
4.  调整应用程序的入口点 (`Program.cs`) 和消息处理，以将请求路由到 Orleans 集群和 Grains。
5.  使用 Orleans 存储提供程序管理 Grain 状态持久性。

有关建议的新架构和重构任务，请参阅 "TelegramSearchBot Orleans 重构方案.md" 文档。
