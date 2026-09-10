# LLM/Agent 架构重构方案

> 目标：把 2024 年式「每 provider 一个上帝类」的旧架构，**一次性整体重写**成现代 agent 架构（参考 pi / pi-ai 的分层），不在旧代码上打补丁。
> 数据库数据必须正确迁移：表结构允许变，数据不能丢。
> 本文档是后续工作的总蓝图（PR #390 已包含阶段 P1/P2，P3 改为整体重写的一部分）。

---

## 一、现状盘点（截至本文档）

### 已完成（在 PR #390 分支上）

| 阶段 | 内容 | 状态 |
|---|---|---|
| P1 共享循环 | `LlmToolLoop` + `ILlmTurnSource` + `LlmStreamEvent` 归一化事件；10 份工具循环 → 1 份 | ✅ 已提交，测试全过 |
| P2 渠道预设 | `LlmProviderCatalog` 11 家预置（含 OpenCode Zen/Go）+ `预制渠道` 指令 + 网关/Key 两步流 | ✅ 已提交 |
| P3 历史管线 | `LlmHistoryQueryService`（批量查询 → `AgentHistoryMessage` rows）+ `LlmPhotoLoader` + `ILLMService.ExecWithHistoryAsync` 接口默认实现 | ⚠️ 骨架已提交，**provider 未接入，worker 未切换** |

### 遗留问题（重构动机）

1. **5 个 provider 上帝类**（~7600 行）：循环虽已共享，但每个 service 仍各自持有：投影、序列化/反序列化历史、工具转换、vision 检查、模型目录、DI 注册——接一个新渠道要动 8 处。
2. **历史五道工序**：SQLite → Redis payload → worker 假 InMemory 库（`SeedTaskDataAsync`）→ 假库查询 → provider 类型。因为老接口契约是"给我消息对象，我自己查库"。
3. **快照格式不统一**：SerializedChatMessage 丢 native tool_call id；OpenAI 用自己的序列化，其他用 tracked history。
4. **双入口**：GeneralLLMController（旧直连）与 AgentChat（队列+worker）两套前端、两套"继续迭代"接线。
5. **状态四存**：同一对话在 SQLite、Redis task-state、Redis snapshot、Redis chunks 四处各存一份、三种格式。

---

## 二、目标架构

```
┌─────────────────────────────────────────────────────────────┐
│ 宿主层（唯一入口）                                            │
│   Telegram 群消息 → AgentChat 触发（唯一入口，删旧直连路径）   │
│   入队：LoadAsync 一次历史（纯 DTO）→ AgentExecutionTask      │
├─────────────────────────────────────────────────────────────┤
│ Agent 层（一份循环，进程内）                                  │
│   AgentLoop.Run(request)：                                    │
│   归一化 LlmMessage 历史 → transport 流式回合 → 工具执行       │
│   → 快照/续跑（schema v2 = 归一化历史）→ 迭代上限              │
├─────────────────────────────────────────────────────────────┤
│ Transport 层（每 API 方言一个适配器，纯转换，无状态）          │
│   ILlmTransport.StreamTurnAsync(LlmTurnRequest) → LlmStreamEvent*│
│   openai-completions │ anthropic-messages │ openai-responses  │
│   │ google-generative-ai │ ollama                             │
├─────────────────────────────────────────────────────────────┤
│ Catalog 层（provider = 数据，非代码）                         │
│   LlmProviderCatalog 预设（内置）+ LLMChannel/LLMApiBinding    │
│   （用户自定义，表结构不变）                                   │
├─────────────────────────────────────────────────────────────┤
│ History 层（一次加载，纯 DTO）                                │
│   LlmHistoryQueryService.LoadAsync → AgentHistoryMessage rows │
│   投影到归一化 LlmMessage：只发生一次，在入队侧                 │
└─────────────────────────────────────────────────────────────┘
```

### 核心决策

| # | 决策 | 理由 |
|---|---|---|
| D1 | **归一化消息模型 `LlmMessage` 贯穿全程**（历史行投影一次 → 循环 → transport 请求 → 快照） | 根治 5×(Serialize/Deserialize)ProviderHistory 与快照丢 tool_call id；接新 provider 只写 transport |
| D2 | **provider service 类消亡**，变为 `LlmTransportRegistry` 按 `LlmProtocol` 分发 | 数据库表（LLMChannel/LLMApiBinding）本来就是 pi 的 Provider/Model 同构体，代码侧跟上 |
| D3 | **历史纯 DTO 贯通**：worker 不查库、不造假库，task.History 直接投影 | 删 `SeedTaskDataAsync`、删 4×投影重复、消灭"五道工序" |
| D4 | **vision 图片在投影时按行从共享磁盘加载**（`LlmPhotoLoader`），不进 Redis payload | 磁盘两进程共享；避免把图片字节塞进队列 |
| D5 | **单入口**：`GeneralLLMController` 的 LLM 执行路径并入 AgentChat 队列路径；`/llm` 类管理命令保留 | 删双份"继续迭代"接线 |
| D6 | **快照 schema v2**：`LlmContinuationSnapshot.ProviderHistory` → `List<LlmMessage>`（归一化），`SchemaVersion=2` | 旧快照 24h TTL 自然过期，无迁移负担 |
| D7 | **worker 进程保留但瘦身**（不取进程合并） | 崩溃隔离对无人值守群聊 bot 有真实价值；瘦身后再评估合并收益 |

### 非目标（明确不做）

- 不改数据库表结构（LLMChannel/LLMApiBinding/Message 均保持）。
- 不做 pi 式 compaction（后续独立提案；当前每任务从 DB 重建历史的语义保留）。
- 不动 OCR/ASR 多进程架构。
- 不做 delta 级 Telegram 流式编辑（Telegram API 限制下全量快照重发是合理妥协）。

---

## 三、目标数据模型（Common 层）

```csharp
// LlmMessage.cs —— 全程唯一的消息表示
public sealed class LlmMessage {
    public LlmRole Role;              // System / User / Assistant / Tool
    public string? Text;              // 文本（Tool 角色 = 工具结果文本）
    public string? Thinking;          // reasoning（assistant）
    public string? ToolCallId;        // Tool 角色
    public List<LlmToolCall>? ToolCalls;   // assistant 发起的调用（归一化，含 id/name/args）
    public byte[]? ImagePng; public string? ImageMediaType;  // user 视觉输入
}

// LlmTurnRequest —— transport 单回合输入
public sealed class LlmTurnRequest {
    public string SystemPrompt;
    public IReadOnlyList<LlmMessage> History;   // 全量归一化历史
    public IReadOnlyList<LlmToolSpec> Tools;    // null = 文本协议
    public LlmTransportConfig Config;           // endpoint/apiKey/model/quirks（来自 channel+binding）
}
```

- `ILlmTransport` 保持 Wave 1 的 `ILlmTurnSource` 语义（StreamTurnAsync / Commit* / GetTrackedHistory → 改为 GetHistory → `List<LlmMessage>`）。
- 快照 v2：`ProviderHistory: List<LlmMessage>` + `SchemaVersion = 2`；resume 时循环直接从归一化历史继续。

---

## 四、执行策略：整体重写，不共存

**不做「新旧接口并存、逐个迁移」的渐进路线。** 一次性新建全部新类型（模型 / 投影 / 循环 / transport / registry），切换所有调用点，然后**删除全部旧代码**（5 个 service 类、ILLMService 旧接口、LLMFactory、turn source 内嵌类）。保留的只有正交基础设施：McpToolHelper（工具注册/执行）、ModelCapabilityService（能力刷新）、OpencodeSessionHeaders、LlmBindingSupport、PromptCachingHelper、LlmVisibilityService。

### 数据迁移映射（表结构保持，语义即迁移）

现有表结构与目标模型同构，无需改表、无需 EF 迁移，数据零丢失：

| 现有数据 | 新架构语义 | 迁移方式 |
|---|---|---|
| `LLMChannel`（Name/Gateway/ApiKey/Provider/Parallel/Priority） | Provider（连接 + 凭证） | 原样读取 |
| `LLMApiBinding`（Endpoint/Protocol/AuthProfile/IsDefault） | Transport 选择（LlmProtocol → transport id）+ 认证方式 | 原样读取；`LlmProtocol` 枚举值不变 |
| `ChannelWithModel`（ModelName/AuthorizationSource/ApiBindingId） | 模型目录（授权集合） | 原样读取 |
| `ModelCapabilities`（CapabilityName/Value，如 vision=true） | 模型能力 | 原样读取（vision/函数调用等名字不变） |
| `Message`/`MessageExtension` | 聊天历史源 | 原样读取（LoadAsync 不变） |
| Redis in-flight 快照（旧 schema v1） | — | 24h TTL 自然过期，重启后作废；不迁移 |
| `GroupSettings.LLMModelName`（群默认模型） | 群 → 模型路由 | 原样读取 |

迁移验证：对现有库跑「旧路径 vs 新路径」同输入投影对比（Phase 3 的投影单测兼做此职），再加一条渠道数据 round-trip 测试（现有 DB 样本 → 新 registry → 能解析出 transport + 模型列表）。

---

## 五、实施阶段（修订）

### Phase 3：历史纯 DTO 贯通（进行中，本 PR 内完成）

**范围**
1. `LlmHistoryQueryService.LoadAsync` 已就绪 → 4 个 provider 的 `GetChatHistory` 改为「LoadAsync + 投影」：
   - 投影循环保留（分组规则/回复格式微差），但名字/扩展取自 row（消除 N+1 查询），图片走 `LlmPhotoLoader`。
2. 每个服务新增 `ExecWithHistoryAsync(rows, message, ..., supportsVision, ...)`：即现 glue 但 `ProjectRows(rows)` 替代 `GetChatHistory` 调用。
3. `LlmServiceProxy`：删 `SeedTaskDataAsync`；`task.History + 输入行` → 直接调 `ExecWithHistoryAsync`；`supportsVision` 从 `task.Channel.Capabilities` 计算。
4. worker 的 `UseInMemoryDatabase` 保留注册（服务构造函数注入需要），但**零播种**。

**验收**
- 单元测试：`ProjectRows` 各 provider 快照比对（OpenAI/Anthropic/Gemini/Responses 各 1 例：给定 rows → 断言投影文本）。
- worker 路径集成测试：构造 task（含 history rows）→ proxy → 断言不触碰 DbContext。
- 全量测试绿 + solution 0 error。

**删除量**：`SeedTaskDataAsync` ~110 行；4×DB 查询管线 ~200 行；N+1 查询消除。

### Phase 4：归一化消息模型 + transport 独立（新 PR 或本 PR 追加，视评审节奏）

**范围**
1. `LlmMessage`/`LlmToolCall`/`LlmToolSpec` 落地 Common；`AgentHistoryMessage` → `LlmMessage` 投影一次成型（`LlmHistoryProjector`，含 vision 图片加载）。
2. Wave 1 的 turn source 类从 service 内嵌迁出为 `Transports/` 下独立类，输入改为 `LlmTurnRequest`（归一化历史），删除各自 `GetTrackedHistory`（快照直接用归一化历史）。
3. `SerializeProviderHistory`/`DeserializeProviderHistory`/`EnsureAlternatingRoles`/`PrepareMessagesForPromptCaching` 收编进各自 transport（外部不再可见）。
4. 快照 v2 切换：循环写入 `LlmMessage` 历史；`ResumeFromSnapshot` 统一为一个共享实现。
5. `ILLMService` 瘦身为 `ExecAsync(LlmAgentRequest)` + 目录方法（GetAllModels/GetAllModelsWithCapabilities/GenerateEmbeddings/AnalyzeImage/IsHealthy）；`LLMFactory` → `LlmTransportRegistry.GetByProtocol(LlmProtocol)`。

**验收**
- 快照 round-trip 单测：native 工具调用 → 快照 → resume，断言 tool_call id 不丢。
- 5 transport 各 1 个「请求构造」单测（不触网，断言 SDK 请求对象字段）。
- 每迁移一个 transport 跑全量测试。

**删除量预估**：序列化/投影重复 ~600 行；服务类 glue ~800 行。

### Phase 3+4 合并：一次重写（本 PR 内完成）

按依赖顺序单次落地，旧代码在最后一步整体删除：

**第 1 步 — 新核心模型（Common/Model/AI/Llm/）**
- `LlmMessage`（Role/Text/Thinking/ToolCalls/ToolCallId/ImagePng）、`LlmToolCall`、`LlmToolSpec`
- `LlmTurnRequest`（SystemPrompt + LlmMessage 历史 + Tools + `LlmTransportConfig`）
- `LlmAgentRequest`（顶层执行请求：chatId/userId/messageId/model/channel/binding/rows/input）

**第 2 步 — 历史投影（一次成型）**
- `LlmHistoryProjector`: `AgentHistoryMessage rows → List<LlmMessage>`（vision 图片按行经 `LlmPhotoLoader` 加载）
- 替代 4×投影 + 4×Serialize/DeserializeProviderHistory + EnsureAlternatingRoles（归一化层处理 user/assistant 交替约束）

**第 3 步 — 循环升级**
- `LlmToolLoop` 改为直接消费 `List<LlmMessage>`（快照 v2 = 归一化历史，`SchemaVersion=2`）
- resume 共享实现一份

**第 4 步 — Transport（5 个，从 Wave 1 turn source 移植）**
- `Transports/OpenAiChatTransport.cs`、`AnthropicMessagesTransport.cs`、`OpenAiResponsesTransport.cs`、`GoogleGenerativeAiTransport.cs`、`OllamaTransport.cs`
- 输入 `LlmTurnRequest`（归一化历史），prompt caching / SDK quirk 全部收编内部
- 附加能力接口（可选实现）：`ILlmModelCatalog`（GetAllModels/WithCapabilities）、`ILlmEmbeddings`、`ILlmImageAnalysis`

**第 5 步 — Registry + 删旧**
- `LlmTransportRegistry.GetByProtocol(LlmProtocol)` / `GetByProvider(LLMProvider)`（替代 LLMFactory）
- 删除：5 个 service 类、`ILLMService`、`LLMFactory`、内嵌 turn source、`LlmContinuationSupport`、各序列化方法
- 调用点切换（全部一次性）：`GeneralLLMController`、`GeneralLLMService`、`CodingAgentReportConsumer`、`LlmServiceProxy`（删 SeedTaskDataAsync）、`FaissVectorService`（embeddings）、`RefreshService`/`ModelCapabilityService`（目录）、`LLMOCRService`/`AltPhotoController`（AnalyzeImage）、`LLMAgentProgram`（DI）

**验收**
- 全量测试绿 + solution 0 error（旧类型全删后编译即证明无残留引用）
- 投影对比测试（现库数据 → 旧格式输出快照固化 vs 新投影）
- 快照 round-trip 测试（native tool_call id 不丢）
- worker 路径：task.History 直达 transport，零 DB 触碰（以 mock DbContext 断言）

### Phase 5：单入口 + 状态收敛（独立 PR）

**范围**
1. `GeneralLLMController` 的 LLM 执行路径改为走 AgentChat 队列（`EnqueueSyntheticMessageTaskAsync` 已具备同等能力）；管理命令（`选择模型` 等）保留原位。
2. 删 `LLMIterationCallbackController` 双份接线 → 一个 continuation 流程。
3. Redis task-state hash 精简：不再重复存完整 payload（BRPOP 消费后状态只留 status/时间戳；恢复场景用 payload 单一来源）。
4. `ChunkPollingService`/快照协议文档化（opencode 线协议已有测试，补充 agent 循环侧）。

**验收**
- `/llm` 与 Agent 聊天两条触发路径端到端手测清单。
- Redis 状态断言测试（入队→消费→状态字段集）。

### Phase 6（可选，独立提案）：worker 进程合并评估

Phase 4 完成后 worker 里只剩「BRPOP + 调 transport」~200 行，届时重新评估：
- 合并进主进程：删 `AgentRegistryService`/`GarnetRpcClient`/心跳/看门狗（~700 行），代价是失去进程隔离。
- 或保留现状（推荐默认，隔离价值真实）。

---

## 五、兼容性与风险

| 风险 | 缓解 |
|---|---|
| 投影行为漂移（prompt 格式变化影响模型行为） | Phase 3 投影单测按 provider 固化现有格式；格式宏差（Gemini ASCII 冒号等）逐字保留 |
| 快照 v2 与线上 in-flight 快照冲突 | 旧快照 24h TTL 自然过期；`SchemaVersion` 检查不匹配 → 提示重新开始 |
| worker `supportsVision` 计算来源变化 | `task.Channel.Capabilities` 与 DB ModelCapabilities 同源（`LoadChannelAsync` 已带），断言测试覆盖 |
| OpenCode 特殊头/目录语义 | `IsOpenCodeBinding` 识别与「目录≠授权」规则在 Phase 3/4 均不触碰，仅迁移载体 |
| 大 PR 评审难 | 整体重写按第 1-5 步分批提交（每步编译+测试绿），单 PR 内可逐步 review |

---

## 六、执行顺序

```
[PR #390 - 本分支]
  P1 共享循环（已完成）
  P2 预设（已完成）
  P3+4 整体重写：LlmMessage → 投影 → 循环 v2 → 5 transports → registry → 删旧  ← 下一步

[PR #391]
  P5 单入口 + Redis 状态收敛

[PR #392?]
  P6 worker 合并评估（默认不动）
```
