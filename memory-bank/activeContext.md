# Active Context - PuppeteerArticleExtractorService Tests

## Current Status
- Attempted to write unit tests for PuppeteerArticleExtractorService
- Initial approach using mocks failed due to:
  - BrowserFetcher is sealed and cannot be mocked
  - Service creates browser instance per request (no field to inject mock)

## Next Steps
1. Refactor service to support dependency injection for testing
2. Or switch to integration testing approach
3. Need to modify test strategy to either:
   - Extract browser creation to separate service that can be mocked
   - Use real browser instance in tests (slower but more reliable)

## Technical Considerations
- PuppeteerSharp requires Chromium installation
- Headless mode should be used for CI environments
- Need to handle browser download/installation in test setup
