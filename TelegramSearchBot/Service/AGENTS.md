# Service Layer

Core business logic implementations for TelegramSearchBot.

## OVERVIEW
Service layer handles all business logic. Controllers delegate to services - services should be stateless.

## STRUCTURE
```
Service/
├── AI/              # OCR, ASR, QR processing
├── BotAPI/          # Telegram API integration
├── Bilibili/        # B站视频/动态处理
├── Common/          # Shared utilities (URL, config)
├── Manage/          # Admin configuration services
├── Scheduler/       # Background tasks & cron jobs
├── Search/          # Search query processing
├── Storage/         # Message persistence
├── Tools/           # MCP tool implementations
└── Vector/          # FAISS vector operations
```

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| Send messages | `BotAPI/SendService.cs` | Reply/edit messages |
| Message storage | `Storage/MessageService.cs` | EF Core operations |
| Vector search | `Vector/FaissVectorService.cs` | FAISS index management |
| Background tasks | `Scheduler/SchedulerService.cs` | Cron-based execution |
| MCP tools | `Tools/*.cs` | 24+ built-in tools |

## KEY INTERFACES
```csharp
IScheduledTask   // Background task (implement with heartbeat)
IService         // Scanned by Scrutor for DI
```

## CONVENTIONS
- Services should be injectable via `[Injectable]`
- Use `PipelineContext.PipelineCache` to receive data from controllers
- Don't hardcode ports - use `Env.SchedulerPort`
- Long-running operations should be async or offloaded to Scheduler

## ANTI-PATTERNS
- Don't store state in services - they may be singleton
- Don't call controllers from services (circular dependency)
- Don't block on async operations without CancellationToken
