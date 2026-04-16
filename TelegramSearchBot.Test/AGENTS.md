# Test Layer

Unit and integration tests for TelegramSearchBot.

## OVERVIEW
Three test projects: TelegramSearchBot.Test, TelegramSearchBot.Search.Test, TelegramSearchBot.LLM.Test

## STRUCTURE
```
TelegramSearchBot.Test/
├── Service/        # Service layer tests
├── View/           # View tests
├── Helper/         # Utility tests
└── RunVectorTests.ps1  # Vector test runner

TelegramSearchBot.Search.Test/    # Search-specific tests
TelegramSearchBot.LLM.Test/       # LLM service tests
```

## FRAMEWORKS
- **Test framework**: xUnit ([Fact], [Theory])
- **Mocking**: Moq for interfaces
- **Database**: EF Core InMemory for isolated DB tests
- **Categories**: `[Trait("Category", "Vector")]` for filtered execution

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| Vector tests | `Service/Vector/*.cs` | FAISS integration |
| DB context | `Service/Database/DataDbContextTests.cs` | Entity mapping |
| LLM tests | `TelegramSearchBot.LLM.Test/` | Service mocking |

## CONVENTIONS
- Use unique database names per test: ` $"InMemoryDb_{Guid.NewGuid()}" `
- Clean up temp files/directories in `[Fact]` cleanup
- Mock `ILogger` and `IServiceProvider` for service tests

## RUN COMMANDS
```bash
# All tests
dotnet test

# Vector tests only
dotnet test --filter "Category=Vector"
pwsh TelegramSearchBot.Test/RunVectorTests.ps1
```
