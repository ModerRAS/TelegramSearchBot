# Coding Agent Tool / 编码 Agent 工具

## Overview / 概览

`run_coding_agent` starts a background `pi --mode rpc` coding job from the LLM tool loop. The tool returns immediately with a job id. A Rust sidecar consumes the job from Garnet/Redis, runs pi through its RPC stdin/stdout protocol, then pushes a report back to the bot. The main process posts the report to Telegram and automatically resumes the LLM loop with that report.

`run_coding_agent` 会从 LLM 工具循环里启动一个后台 `pi --mode rpc` 编码任务。工具会立即返回 job id。Rust sidecar 从 Garnet/Redis 消费任务，通过 pi 的 RPC stdin/stdout 协议运行任务，然后把报告推回 bot。主进程会把报告发到 Telegram，并基于报告自动续跑 LLM。

## Architecture / 架构

1. LLM calls `run_coding_agent(prompt, workingDirectory, ...)`.
2. Main process validates the chat whitelist and workspace path guardrails.
3. Main process writes `CodingAgentJobRequest` to `CODING_AGENT_JOBS`.
4. `telegramsearchbot-coding-agent-sidecar` runs pi in RPC mode and writes logs under the TelegramSearchBot work directory.
5. Sidecar writes `CodingAgentJobReport` to `CODING_AGENT_REPORTS`.
6. Main process sends a Telegram report and resumes the LLM task automatically.

1. LLM 调用 `run_coding_agent(prompt, workingDirectory, ...)`。
2. 主进程校验群白名单和工作目录护栏。
3. 主进程把 `CodingAgentJobRequest` 写入 `CODING_AGENT_JOBS`。
4. `telegramsearchbot-coding-agent-sidecar` 以 RPC 模式运行 pi，并把日志写入 TelegramSearchBot 工作目录。
5. Sidecar 把 `CodingAgentJobReport` 写入 `CODING_AGENT_REPORTS`。
6. 主进程发送 Telegram 报告并自动恢复 LLM 任务。

## Config / 配置

Add these fields to `%LOCALAPPDATA%/TelegramSearchBot/Config.json`:

```json
{
  "EnableCodingAgentTool": true,
  "CodingAgentAllowedGroupIds": [-1001234567890],
  "CodingAgentDefaultTimeoutMinutes": 60,
  "CodingAgentMaxConcurrentJobs": 2,
  "CodingAgentMaxAutoResumeContinuations": 4,
  "CodingAgentPiCommand": "pi",
  "CodingAgentSidecarCommand": "telegramsearchbot-coding-agent-sidecar",
  "CodingAgentDeniedPathPrefixes": []
}
```

- `EnableCodingAgentTool`: disabled by default. / 默认关闭。
- `CodingAgentAllowedGroupIds`: explicit Telegram group whitelist. Empty means no group can use it. / Telegram 群白名单；空列表表示没有群可用。
- `CodingAgentDeniedPathPrefixes`: extra denied path prefixes. Built-in defaults deny app config/secrets and OS directories. / 额外拒绝路径前缀；内置默认会拒绝应用配置/密钥目录和系统目录。
- `CodingAgentSidecarCommand`: sidecar binary path or command name. / sidecar 二进制路径或命令名。

Build the sidecar:

```powershell
cargo build --manifest-path TelegramSearchBot.CodingAgentSidecar/Cargo.toml --release
```

Put the resulting `telegramsearchbot-coding-agent-sidecar.exe` on `PATH`, next to the bot executable, or set `CodingAgentSidecarCommand` to its full path.

把生成的 `telegramsearchbot-coding-agent-sidecar.exe` 放到 `PATH`、bot 可执行文件旁边，或把 `CodingAgentSidecarCommand` 设置为完整路径。

## Multi-Agent / 多 Agent

The optional `agents` parameter accepts JSON:

```json
[
  { "name": "implementer", "prompt": "Implement the requested change." },
  { "name": "reviewer", "role": "Review the change and report risks." }
]
```

Agents run sequentially in the same workspace to avoid conflicting edits. Each agent gets its own pi session directory.

多 agent 会在同一个工作目录里顺序运行，避免并发修改互相冲突。每个 agent 都有独立的 pi session 目录。
