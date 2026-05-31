# TelegramSearchBot rmux/pi sidecar

中文：这个 sidecar 从 TelegramSearchBot 的 Garnet/Redis 队列接收 `run_coding_agent` 任务，在后台调用 `pi --mode rpc`，并用 rmux 创建可观察的日志会话。任务结束后，sidecar 把报告推回 Redis，主进程会发送 Telegram 报告并自动续跑 LLM。

English: this sidecar consumes `run_coding_agent` jobs from TelegramSearchBot's Garnet/Redis queue, runs `pi --mode rpc` in the background, and creates rmux log sessions for inspection. When a job finishes, it pushes a report back to Redis so the main process can post the Telegram report and resume the LLM loop.

Build:

```powershell
cargo build --manifest-path TelegramSearchBot.RmuxSidecar/Cargo.toml --release
```

Config:

- `EnableCodingAgentTool`: must be `true`.
- `CodingAgentAllowedGroupIds`: explicit Telegram group whitelist.
- `CodingAgentSidecarCommand`: path/name of this binary. Defaults to `telegramsearchbot-rmux-sidecar`.
- `CodingAgentPiCommand`: path/name of `pi`. Defaults to `pi`.

The sidecar uses rmux for detached observation/log panes and direct pi RPC over stdin/stdout for protocol correctness.
