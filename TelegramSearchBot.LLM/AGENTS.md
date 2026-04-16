# LLM Layer

LLM services, MCP client/manager, and built-in tool implementations.

## OVERVIEW
Manages LLM providers (OpenAI, Ollama, Gemini, Anthropic) and MCP tool integration.

## STRUCTURE
```
TelegramSearchBot.LLM/
├── Interface/          # I*Service interfaces
├── Model/              # DTOs and models
└── Service/
    ├── AI/LLM/         # LLM provider implementations
    ├── Mcp/            # MCP client & server manager
    └── Tools/          # Built-in tool services
```

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| LLM factory | `Service/AI/LLM/LLMFactory.cs` | Create LLM instances |
| MCP client | `Service/Mcp/McpClient.cs` | MCP protocol client |
| Tool registration | `Service/AI/LLM/McpToolHelper.cs` | Scan [BuiltInTool] |
| OpenAI service | `Service/AI/LLM/OpenAIService.cs` | OpenAI API wrapper |

## KEY PATTERNS

### Built-in Tool Definition
```csharp
[BuiltInTool(Name = "tool_name", Description = "...")]
public async Task<string> ToolMethod(
    [BuiltInParameter(Name = "param")] string value) {
    // Implementation
}
```

### LLM Provider
```csharp
public class MyLLMService : ILLMService {
    public async Task<string> GenerateAsync(string prompt, ...) { }
}
```

## CONVENTIONS
- Tools must be in same assembly for auto-registration
- Use `[BuiltInTool]` / `[BuiltInParameter]` (NOT deprecated `[McpTool]`)
- LLM services implement interface from `Interface/` folder
- MaxToolCycles defaults to 25 to prevent infinite loops

## MCP INTEGRATION
- MCP servers managed by `McpServerManager`
- Tools exposed via MCP protocol to LLM
- External servers configured via bot commands
