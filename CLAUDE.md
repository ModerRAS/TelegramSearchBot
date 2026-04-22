# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview
TelegramSearchBot is a .NET 10.0 console application that provides Telegram bot functionality for group chat message storage, search, and AI processing. It supports traditional keyword search via Lucene.NET and semantic search via FAISS vectors.

## Architecture
- **Message Processing Pipeline**: IOnUpdate-based controller pattern with dependency-based topological sorting
- **Storage**: SQLite + EF Core 10.0 for data, Lucene.NET for full-text search, FAISS for vectors
- **AI Services**: OCR (PaddleOCR), ASR (Whisper), LLM (Ollama/OpenAI/Gemini/Anthropic)
- **Multi-Modality**: Handles text, images, audio, video with automatic content extraction
- **Background Tasks**: IScheduledTask-based scheduler with heartbeat monitoring
- **MCP Integration**: Model Context Protocol support for external tool servers (24+ built-in tools)
- **Multi-Process**: OCR/ASR run as separate processes managed by AppBootstrap (Windows JobObject)

## Solution Structure
```
TelegramSearchBot.sln (8 projects)
├── TelegramSearchBot              # Main console app (entry: Program.cs)
├── TelegramSearchBot.Common       # Shared config (Env.cs), attributes, models
├── TelegramSearchBot.Database     # EF Core DbContext, migrations
├── TelegramSearchBot.Search       # Lucene.NET search engine封装
├── TelegramSearchBot.LLM          # LLM services, MCP client/manager
├── TelegramSearchBot.Test         # Core unit/integration tests
├── TelegramSearchBot.Search.Test  # Search engine tests
└── TelegramSearchBot.LLM.Test    # LLM service tests
```

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| Bot commands | `Controller/` | Implement IOnUpdate, declare Dependencies |
| Search logic | `Service/Search/` + `TelegramSearchBot.Search/` | Lucene + vector hybrid |
| AI/LLM | `Service/AI/LLM/` + `TelegramSearchBot.LLM/` | Ollama/OpenAI/Gemini |
| Vector search | `Service/Vector/` | FaissVectorService, ConversationVectorService |
| Config | `TelegramSearchBot.Common/Env.cs` | Config.json at %LOCALAPPDATA%/TelegramSearchBot/ |
| MCP tools | `TelegramSearchBot.LLM/Service/Tools/` | BuiltInToolAttribute-marked methods |
| Scheduled tasks | `Service/Scheduler/` | Implement IScheduledTask with heartbeat |

## KEY PATTERNS

### Controller (IOnUpdate) Pattern
```csharp
public class MyController : IOnUpdate {
    public List<Type> Dependencies => new() { typeof(DependencyController) };
    
    public async Task OnUpdate(Update update, PipelineContext context) {
        // Use context.PipelineCache to share data across controllers
    }
}
```

### Service Registration
- DI via Scrutor scanning: `IOnUpdate`, `IService`, `IView`
- Use `[Injectable(ServiceLifetime.Singleton)]` attribute
- Services should be namespace under TelegramSearchBot for scanning

### MCP Tool Definition
```csharp
[BuiltInTool(Name = "tool_name", Description = "...")]
public async Task<string> ToolMethod([BuiltInParameter(Name = "param")] string value) {
    // Tool implementation
}
```

### Background Task
```csharp
[Injectable(ServiceLifetime.Singleton)]
public class MyTask : IScheduledTask {
    public string CronExpression => "0 * * * *"; // hourly
    
    public async Task ExecuteAsync(CancellationToken ct) {
        SetHeartbeatCallback(() => { /* keep alive */ });
        // Task logic
    }
}
```

## BUILD COMMANDS
```bash
# Restore & Build
dotnet restore TelegramSearchBot.sln
dotnet build TelegramSearchBot.sln --configuration Release

# Run tests
dotnet test
dotnet test --filter "Category=Vector"  # Vector-specific tests
pwsh TelegramSearchBot.Test/RunVectorTests.ps1

# Publish
dotnet publish -r win-x64 --self-contained
```

## DEFAULT PR WORKFLOW
- Unless the user explicitly asks to reuse an existing branch or continue an existing PR branch, sync the latest `origin/master` first and create a fresh branch from updated `master`.
- Investigate and implement only the requested scope, then run the existing validation commands relevant to the touched area.
- Push the branch and create or update the pull request as the default end-to-end flow.
- Always inspect PR CI status. If a check fails, read the failing job logs, fix the reported issue, and push follow-up commits.
- Always inspect the PR conversation, review comments, and related discussion. If feedback requires code changes, make those changes and update the PR instead of only replying in text.
- If work is blocked by missing permissions, an external outage, or ambiguous requirements, state the blocker clearly.
- After the workflow is complete, use the `ask_user` tool to ask what to do next.

## Development Notes
- **Platform**: Windows primary, Linux partial (OCR/ASR limited)
- **Config**: `%LOCALAPPDATA%/TelegramSearchBot/Config.json` - DO NOT use appsettings.json
- **Logging**: Serilog (console + file + OpenTelemetry)
- **Database**: SQLite at `Env.WorkDir/Data.sqlite`, EF Core migrations in `Migrations/`
- **Vector indexes**: `Env.WorkDir/faiss_indexes/`

## Anti-Patterns (THIS PROJECT)
1. **Don't hardcode ports** - Use `Env.SchedulerPort` for Garnet/Redis connections
2. **Don't use static vars for cross-controller data** - Use `PipelineContext.PipelineCache`
3. **Don't forget Dependencies** - Missing declarations cause "Circular dependency detected"
4. **Don't skip heartbeat in scheduled tasks** - Will be marked as stuck
5. **Don't modify vector structure without rebuilding** - Run `/faiss重建` after changes

## Common Gotchas
- New controllers need namespace in TelegramSearchBot for DI scanning to work
- Static Env properties require Config.json in correct location
- MCP tools must be in same assembly for auto-registration
- Long-running tasks (OCR/download) should go to Service/Scheduler or separate process
- When modifying config, update corresponding Docs/README_*.md files

## Existing Documentation
- `.github/copilot-instructions.md` - Detailed Chinese dev guide (read this first)
- `Docs/Bot_Commands_User_Guide.md` - User-facing command reference
- `Docs/Architecture_Overview.md` - System architecture
- `Docs/Build_and_Test_Guide.md` - Build/test instructions
