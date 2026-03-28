# TelegramSearchBot - Agent Development Guide

## Build Commands

### Full Solution
```bash
dotnet restore TelegramSearchBot.sln
dotnet build TelegramSearchBot.sln -c Release
dotnet test -c Release
```

### Individual Projects
```bash
dotnet build TelegramSearchBot.Tokenizer/TelegramSearchBot.Tokenizer.csproj -c Release
dotnet build TelegramSearchBot.Search/TelegramSearchBot.Search.csproj -c Release
```

### Running Single Tests
```bash
# By project
dotnet test TelegramSearchBot.Tokenizer.Tests/TelegramSearchBot.Tokenizer.Tests.csproj -c Release

# By test class
dotnet test --filter "FullyQualifiedName~SmartChineseTokenizerTests" -c Release

# By specific test
dotnet test --filter "FullyQualifiedName~TokenizerFactoryTests.Create_SmartChinese_Returns" -c Release

# Vector tests (Windows PowerShell)
pwsh TelegramSearchBot.Test/RunVectorTests.ps1
```

### Code Formatting
```bash
# Check formatting (CI uses this)
dotnet format --verify-no-changes

# Auto-fix formatting
dotnet format
```

### Security & Quality
```bash
# Check vulnerable packages
dotnet list package --vulnerable --include-transitive

# Run specific analyzer
dotnet build -c Release /p:EnforceCodeStyleInBuild=true
```

## Project Structure

```
TelegramSearchBot.sln
├── TelegramSearchBot/              # Main console app
├── TelegramSearchBot.Common/       # Shared models, Env.cs config
├── TelegramSearchBot.Database/      # EF Core, Migrations
├── TelegramSearchBot.Search/        # Lucene/FAISS search
├── TelegramSearchBot.Search.Test/
├── TelegramSearchBot.LLM/           # AI/LLM services
├── TelegramSearchBot.LLM.Test/
├── TelegramSearchBot.Test/          # Main test suite
├── TelegramSearchBot.Tokenizer/     # Tokenizer abstraction layer
└── TelegramSearchBot.Tokenizer.Tests/
```

### Key Directories
- **Controller/** - Bot command handlers (implement `IOnUpdate`)
- **Service/** - Business logic services
- **Model/** - EF entities and DTOs
- **Service/Storage/** - Database operations via EF Core
- **Service/Scheduler/** - Background tasks (`IScheduledTask`)
- **Service/AI/LLM/** - AI model integration
- **Service/BotAPI/** - Telegram bot API handling
- **Search/Tool/** - Lucene/FAISS search utilities
- **Manager/** - Core managers (LuceneManager, etc.)

## Code Style Guidelines

### Formatting (from .editorconfig)
- **Indent**: 4 spaces (no tabs)
- **Braces**: K&R style - opening brace on same line
- **Binary operators**: Space before and after (`a + b`)
- **Keywords in control flow**: Space after (`if (x)`)
- **Type casts**: Space after (`(string)x`)
- **Method declarations**: No space inside parentheses
- **Expressions in parentheses**: Space between

```csharp
// Correct
if (condition) {
    doSomething();
}
var result = (string)value;

// Incorrect
if (condition)
{
    doSomething();
}
var result = (string) value;
```

### Naming Conventions
- **Classes/Interfaces**: PascalCase (`LuceneManager`, `ITokenizer`)
- **Methods**: PascalCase (`GetTokenize`, `FindBestSnippet`)
- **Private fields**: `_camelCase` with underscore prefix (`_tokenizer`, `_logAction`)
- **Parameters**: camelCase (`query`, `groupId`)
- **Constants**: PascalCase (`MaxRetryCount`)
- **Test methods**: `MethodName_Scenario_ExpectedResult`

### Using Statements
- System directives first, then project imports
- No blank line between groups
- Remove unused usings before committing

### Error Handling
- Never swallow exceptions silently (`catch {}` or `catch (Exception) {}` is forbidden)
- Always log or rethrow with context
- Use `Action<string>?` for optional logging

```csharp
// Correct
try {
    // operation
} catch (Exception ex) {
    _logAction?.Invoke($"Failed: {ex.Message}");
    throw;
}

// Incorrect
try {
    // operation
} catch {
    // silently ignored
}
```

### Type Safety
- **Never** use `as any`, `@ts-ignore`, `@ts-expect-error`
- Prefer `IReadOnlyList<T>` for collections in interfaces
- Use records for immutable data transfer objects
- Nullable reference types enabled - respect nullability

### DI & Architecture
- Controllers should be stateless
- Use constructor injection
- New services add `[Injectable]` attribute
- Background tasks implement `IScheduledTask`

## Testing Guidelines

### Test Naming
```csharp
[Fact]
public void Tokenize_ReturnsTokens_ForChineseText() { }

[Theory]
[InlineData("input", "expected")]
public void Parse_Handles_InputCase(string input, string expected) { }
```

### Test Structure
1. Arrange - set up test data
2. Act - perform the operation
3. Assert - verify results

### Test Coverage
- New features require tests
- Bug fixes should include regression tests
- Edge cases (empty, null, overflow) must be covered

## Common Patterns

### Creating a New Controller
```csharp
public class MyController : IOnUpdate {
    public List<Type> Dependencies => new();
    public async Task OnUpdate(Update update, PipelineContext context) {
        // implementation
    }
}
```

### Adding a Scheduled Task
```csharp
[Injectable(ServiceLifetime.Singleton)]
public class MyTask : IScheduledTask {
    public string CronExpression => "0 * * * *"; // hourly
    public async Task ExecuteAsync() {
        // implementation
    }
}
```

### Using the Tokenizer
```csharp
public class MyService {
    private readonly ITokenizer _tokenizer;
    public MyService(ITokenizer tokenizer) => _tokenizer = tokenizer;

    public void Process(string text) {
        var tokens = _tokenizer.Tokenize(text);
        var safe = _tokenizer.SafeTokenize(text);
        var withOffsets = _tokenizer.TokenizeWithOffsets(text);
    }
}
```

## CI Requirements

Before pushing:
1. `dotnet format --verify-no-changes` must pass
2. All tests must pass
3. Build must succeed in Release configuration

## Configuration

- Config file: `%LOCALAPPDATA%/TelegramSearchBot/Config.json`
- Work directory: `Env.WorkDir`
- Redis/Garnet: `Env.SchedulerPort`
- DO NOT hardcode ports or paths

## Documentation

When adding new features:
- Update relevant docs in `Docs/`
- Add XML doc comments to public APIs
- Update this file if adding new project types
