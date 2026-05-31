# TelegramSearchBot - MCP (Model Context Protocol) 文档

## 一、概述

MCP (Model Context Protocol) 是一种让LLM与外部工具和服务交互的协议。TelegramSearchBot通过MCP集成了丰富的内置工具，并支持添加外部MCP服务器来扩展功能。

### 架构图

```
┌─────────────────────────────────────────────────────────────┐
│                      LLM (Ollama/OpenAI/Gemini)              │
└────────────────────────────┬────────────────────────────────┘
                             │ Tool Calls
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                    McpToolHelper                             │
│  - 扫描 BuiltInToolAttribute 标记的工具                     │
│  - 生成工具 XML 描述                                        │
│  - 管理外部 MCP 服务器                                       │
└─────────────┬───────────────────────────────────────────────┘
              │
    ┌─────────┴─────────┐
    ▼                   ▼
┌─────────┐      ┌─────────────────┐
│ 内置工具 │      │  外部MCP服务器   │
│ (30+)   │      │  (可动态添加)    │
└─────────┘      └─────────────────┘
```

## 二、内置工具列表

TelegramSearchBot 内置了以下工具，通过 `BuiltInToolAttribute` 标记注册：

### 2.1 文件操作工具（仅管理员）

| 工具名称 | 描述 | 参数 |
|---------|------|------|
| `ReadFile` | 读取文件内容，支持指定行范围 | `path`, `start_line?`, `end_line?` |
| `WriteFile` | 写入内容到文件，文件不存在则创建 | `path`, `content` |
| `SearchText` | 使用正则表达式搜索文件 | `pattern`, `path?`, `file_glob?`, `ignore_case?` |
| `ListFiles` | 列出目录下的文件和子目录 | `path?`, `pattern?` |

### 2.2 Telegram 发送工具

| 工具名称 | 描述 | 参数 |
|---------|------|------|
| `send_photo_base64` | 发送图片（使用base64编码） | `base64_data`, `caption?`, `reply_to_message_id?` |
| `send_photo_file` | 发送图片（使用文件路径） | `file_path`, `caption?`, `reply_to_message_id?` |
| `send_video_file` | 发送视频（使用文件路径） | `file_path`, `caption?`, `reply_to_message_id?` |
| `send_document_file` | 发送文件（使用文件路径） | `file_path`, `caption?`, `reply_to_message_id?` |
| `send_message` | 发送文本消息 | `chat_id`, `text` |

### 2.2.1 图片生成工具

| 工具名称 | 描述 | 参数 |
|---------|------|------|
| `generate_image` | 通过配置的图片 API 生成图片，默认模型 `gpt-image-2`；OpenAI-compatible 模型走 `/v1/images/generations`，MiniMax `image-01` / `image-01-live` 走 `/v1/image_generation`；图片保存到本地并可直接发送到当前聊天 | `prompt`, `model?`, `size?`, `quality?`, `output_format?`, `background?`, `moderation?`, `aspect_ratio?`, `minimax_response_format?`, `seed?`, `prompt_optimizer?`, `aigc_watermark?`, `style_type?`, `style_weight?`, `count?`, `send_to_chat?`, `caption?`, `reply_to_message_id?`, `timeout_seconds?` |

管理员命令：
- `开启生图工具`：允许 `generate_image` 注入到 LLM 工具提示词和 native tool definitions。
- `关闭生图工具`：隐藏 `generate_image`，关闭后 LLM 不会看到该工具。
- `生图工具状态`：查看开关状态和默认生图模型。
- `设置生图模型`：设置默认生图模型，默认 `gpt-image-2`；MiniMax 可设置为 `image-01` 或 `image-01-live`。

API 地址和 API Key 来自该模型关联的 LLM 渠道，因此可通过 `新建渠道` / `编辑渠道` 自定义网关地址，例如 `https://api.openai.com/v1` 或 `https://api.minimaxi.com`。配置 MiniMax 时，渠道类型建议选择 `MiniMax`，再通过 `添加模型` 关联 `image-01` / `image-01-live`。

MiniMax 不支持选择输出文件格式；`output_format` 仅用于 OpenAI-compatible 图片接口。MiniMax 的 URL 响应会按实际 Content-Type 或 URL 扩展名保存，base64 响应默认按 PNG 保存。

**文件大小限制**:
- 本地 Bot API（内置或外部）: 最大 2GB
- 云端API: 最大 50MB

### 2.3 搜索工具

| 工具名称 | 描述 | 参数 |
|---------|------|------|
| `search_messages` | 在当前聊天的索引消息中搜索关键词 | `query`, `page?`, `page_size?` |
| `query_messages` | 在消息历史数据库中查询，支持多种过滤条件 | `query_text?`, `sender_user_id?`, `sender_name_hint?`, `start_date?`, `end_date?`, `page?`, `page_size?` |

### 2.4 短链接工具

| 工具名称 | 描述 | 参数 |
|---------|------|------|
| `expand_short_url` | 获取短链接的完整URL | `short_url` |
| `list_short_urls` | 列出所有短链接映射 | `original_url_query?`, `expanded_url_query?`, `page?`, `page_size?` |
| `expand_short_urls_batch` | 批量获取短链接的完整URL | `short_urls` |

### 2.5 MCP 服务器管理工具（仅管理员）

| 工具名称 | 描述 | 参数 |
|---------|------|------|
| `ListMcpServers` | 列出所有已配置的MCP服务器 | - |
| `AddMcpServer` | 添加新的MCP服务器 | `name`, `command`, `args`, `env?` |
| `RemoveMcpServer` | 移除指定的MCP服务器 | `name` |
| `UpdateMcpServer` | 更新MCP服务器配置 | `name`, `timeout?`, `enabled?`, `env?`, `command?`, `args?` |
| `RestartMcpServers` | 重启所有MCP服务器 | - |

### 2.6 其他工具

| 工具名称 | 描述 | 参数 |
|---------|------|------|
| `process_memory_command` | 在记忆图谱中执行操作 | `command`, `arguments` |
| `search_web` | 使用 Brave Search API 进行网页搜索 | `query`, `page?`, `count?`, `country?`, `search_lang?` |
| `extract_article` | 使用 Puppeteer 提取网页文章内容 | `url` |
| `sequential_thinking` | 动态问题解决思考工具 | `input`, `next_thought_needed?`, `thought_number?`, `total_thoughts?`, ... |
| `bash` | 执行 Shell 命令 | `command`, `working_directory?`, `timeout_ms?` |
| `execute_js` | 使用 Deno 执行 JavaScript 代码 | `js_code`, `timeout_ms?` |
| `echo` | 回显文本（测试用） | `text` |
| `calculator` | 计算算术表达式 | `expression` |
| `create_todo_item` | 创建待办事项 | `title`, `list_name?`, `description?`, `priority?`, `due_at?`, `remind_at?` |
| `query_todo_items` | 查询待办事项 | `list_name?`, `status?`, `page?`, `page_size?` |
| `update_todo_item` | 更新待办事项 | `todo_id`, `title?`, `list_name?`, `description?`, `priority?`, ... |
| `complete_todo_item` | 标记待办事项完成 | `todo_id` |

## 三、外部 MCP 服务器

### 3.1 添加外部 MCP 服务器

管理员可以通过 `AddMcpServer` 工具添加外部 MCP 服务器：

```
使用示例：
- 名称: my-mcp-server
- 命令: npx
- 参数: -y @modelcontextprotocol/server-filesystem
- 环境变量: { "PATH": "/usr/local/bin:/usr/bin" }
```

### 3.2 MCP 服务器配置参数

| 参数 | 说明 | 示例 |
|------|------|------|
| `name` | 服务器名称 | `filesystem-server` |
| `command` | 启动命令 | `npx`, `python`, `node` |
| `args` | 命令参数列表 | `["-y", "@modelcontextprotocol/server-filesystem"]` |
| `env` | 环境变量字典 | `{ "KEY": "VALUE" }` |
| `timeout_seconds` | 超时时间（秒） | `30` |

## 四、LLM 工具调用迭代限制

为防止 LLM 无限调用工具导致死循环，系统实现了迭代限制机制：

### 4.1 工作原理

1. 每次 LLM 调用工具时，计数器递增
2. 当达到 `MaxToolCycles` 限制（默认 25 次）时：
   - 系统保存当前对话快照到 Redis
   - 向用户显示 "继续迭代" 和 "停止" 按钮
3. 用户选择后可继续或结束当前对话

### 4.2 配置

在 `Config.json` 中设置：

```json
{
  "EnableLLMAgentProcess": false,
  "AgentHeartbeatIntervalSeconds": 10,
  "AgentHeartbeatTimeoutSeconds": 60,
  "AgentChunkPollingIntervalMilliseconds": 200,
  "AgentIdleTimeoutMinutes": 15,
  "MaxConcurrentAgents": 8,
  "AgentTaskTimeoutSeconds": 300,
  "AgentShutdownGracePeriodSeconds": 15,
  "AgentMaxRecoveryAttempts": 2,
  "AgentQueueBacklogWarningThreshold": 20,
  "AgentProcessMemoryLimitMb": 256,
  "MaxToolCycles": 25
}
```

- `EnableLLMAgentProcess=true` 时，LLM 对话循环会迁移到独立 Agent 进程，主进程仅负责 Telegram 收发、任务队列和流式转发。
- `AgentHeartbeatIntervalSeconds` / `AgentHeartbeatTimeoutSeconds` 控制主进程对 Agent 存活状态的检测。
- `AgentChunkPollingIntervalMilliseconds` 控制主进程从 Garnet 轮询流式输出块的频率。
- `AgentIdleTimeoutMinutes` / `AgentShutdownGracePeriodSeconds` 控制 Agent 的空闲回收和优雅停机窗口。
- `MaxConcurrentAgents` / `AgentProcessMemoryLimitMb` 用于约束 Agent 并发数量和内存占用。
- `AgentTaskTimeoutSeconds` / `AgentMaxRecoveryAttempts` 控制任务超时后的重试和死信恢复策略。

## 五、安全考虑

### 5.1 管理员专用工具

以下工具仅对管理员开放：
- 所有文件操作工具 (`ReadFile`, `WriteFile`, `EditFile`, `SearchText`, `ListFiles`)
- MCP 服务器管理工具 (`AddMcpServer`, `RemoveMcpServer`, `UpdateMcpServer`, `RestartMcpServers`)
- `bash` 命令执行工具

### 5.2 MCP 服务器隔离

- 每个 MCP 服务器运行在独立进程中
- MCP 客户端实现进程监控，服务器异常退出时会自动重连
- 服务器通信使用 JSON-RPC 2.0 协议

## 六、故障排除

### 6.1 MCP 服务器无响应

1. 使用 `ListMcpServers` 检查服务器状态
2. 使用 `RestartMcpServers` 重启所有服务器
3. 检查服务器日志输出

### 6.2 工具调用超时

- 增加 `timeout_seconds` 配置
- 检查网络连接
- 确认目标服务器资源充足

### 6.3 工具未出现在列表中

1. 确认服务已正确注册（检查 `[BuiltInTool]` 属性）
2. 确认服务类在主程序集或被扫描的命名空间内
3. 重启机器人使新工具生效

## 七、相关文档

- [用户指令指南](Bot_Commands_User_Guide.md)
- [系统架构概览](Existing_Codebase_Overview.md)
- [向量搜索文档](README_FaissVectorSearch.md)
