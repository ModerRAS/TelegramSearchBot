# Search Layer

Lucene.NET full-text search engine and query processing.

## OVERVIEW
Encapsulates Lucene.NET for keyword search, with support for advanced query syntax.

## STRUCTURE
```
TelegramSearchBot.Search/
├── Model/          # DTOs (SearchMessageDTO, SearchType)
├── Service/        # Search service implementations
├── Tokenizer/      # Text tokenization
└── Tool/           # LuceneManager, query builders
```

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| Lucene wrapper | `Tool/LuceneManager.cs` | Index read/write |
| Search service | `Service/SimpleSearchService.cs` | Query execution |
| Query building | `Tool/*QueryBuilder.cs` | Query construction |

## CONVENTIONS
- Index stored in `Env.WorkDir/lucene_index/`
- Use `DocumentMessageMapper` to convert EF entities to Lucene docs
- Search results cached via `SearchPageCacheCleanupTask`

## ANTI-PATTERNS
- Don't modify index structure without rebuilding (run `/重建索引`)
- Don't call LuceneManager from controllers - use Service/Search layer
