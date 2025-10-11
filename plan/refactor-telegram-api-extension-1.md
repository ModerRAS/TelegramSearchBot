# TelegramSearchBot.BotAPI Extraction Plan

This plan tracks the TODO list to extract the message sending pipeline into the new `TelegramSearchBot.BotAPI` library. See `Docs/消息发送拆分方案.md` for background and motivation.

## Phase 1 – Project Scaffolding (4 tasks)

- [ ] Create `TelegramSearchBot.BotAPI` class library targeting `net9.0` (same as main projects).
- [ ] Add the project into `TelegramSearchBot.sln` under a `BotAPI` solution folder.
- [ ] Reference shared packages (`Telegram.Bot`, `Microsoft.Extensions.*`, `HtmlAgilityPack`, `Markdig`, `System.Threading.RateLimiting`).
- [ ] Add project reference from `TelegramSearchBot` to `TelegramSearchBot.BotAPI` and share `TelegramSearchBot.Common`.

## Phase 2 – Manager Layer Migration (4 tasks)

- [ ] Move rate limiter queue manager into `TelegramSearchBot.BotAPI.Manager.SendMessageManager` (keep behaviour).
- [ ] Extract configuration types used by the manager into `TelegramSearchBot.BotAPI`.
- [ ] Add unit tests (or move existing) covering the queue throttling logic.
- [ ] Update main project to use manager from the new package.

## Phase 3 – Helper Layer Migration (3 tasks)

- [ ] Port `MessageFormatHelper` into `TelegramSearchBot.BotAPI.Helper` namespace.
- [ ] Ensure Markdown-to-HTML conversion stays backwards compatible (Serilog logging, HTML sanitising).
- [ ] Provide internal extension points for formatting to avoid main project dependencies.

## Phase 4 – Interface Layer Migration (5 tasks)

- [ ] Introduce `IBotApiService` marker plus existing send message service interfaces.
- [ ] Update DI registration to use interfaces from the new project.
- [ ] Adjust any existing implementations to consume new interfaces.
- [ ] Ensure DI scanning (Scrutor) still finds the services.
- [ ] Add docs/comments for the new interfaces.

## Phase 5 – Service Layer Migration (6 tasks)

- [ ] Move `SendMessageService` (core, standard, streaming) to the new project.
- [ ] Preserve constructor signatures with optional injection adjustments.
- [ ] Extract shared DTOs/models and keep them Telegram SDK based.
- [ ] Update usages in the main project to reference the new namespace.
- [ ] Add or move service-level tests.
- [ ] Verify Serilog/Otel logging continues to work through DI.

## Phase 6 – View Layer Migration (7 tasks)

- [ ] Move all `View/*` types (image, streaming, video, generic, word cloud, search, etc.).
- [ ] Replace direct project model dependencies with abstractions or Telegram SDK types.
- [ ] Ensure fluent APIs remain source/binary compatible.
- [ ] Add minimal tests (snapshot/string) for key views if feasible.
- [ ] Update references in controllers/services to new namespaces.
- [ ] Check for reflection or `typeof` references that need updates.
- [ ] Confirm resource files/images paths still resolve after move.

## Phase 7 – Dependency Injection (5 tasks)

- [ ] Author `IServiceCollection AddTelegramBotAPI(this IServiceCollection services)` extension.
- [ ] Register rate limiter, services, views, and helpers explicitly to avoid Scrutor scanning gaps.
- [ ] Integrate extension into `ConfigureAllServices`.
- [ ] Ensure configuration/Env dependencies are sourced from `TelegramSearchBot.Common` only.
- [ ] Provide optional configuration hooks (builder pattern / options).

## Phase 8 – Main Project Integration & Cleanup (7 tasks)

- [ ] Remove moved files from the main project.
- [ ] Fix all namespace references and using statements.
- [ ] Ensure `GeneralBootstrap` and controllers compile referencing new library.
- [ ] Clean up obsolete folders (Manager/Service/View helper duplicates).
- [ ] Update project files to drop now-unused package references.
- [ ] Ensure build succeeds locally in Release.
- [ ] Verify migrations/tests referencing removed files are adjusted.

## Phase 9 – Testing & Validation (6 tasks)

- [ ] Run unit tests (`dotnet test`).
- [ ] Execute integration tests or targeted scripts (`TelegramSearchBot.Test`).
- [ ] Validate manual scenario sending various message types via a smoke test stub.
- [ ] Confirm rate limiter behaviour through logs/test harness.
- [ ] Check DI container for missing service registrations.
- [ ] Document test results in repo (appendix in docs if needed).

## Phase 10 – Documentation (5 tasks)

- [ ] Update `Docs/` guides referencing moved namespaces.
- [ ] Document new extension method usage (`AddTelegramBotAPI`).
- [ ] Provide migration notes for downstream integrators.
- [ ] Update any architecture diagrams impacted.
- [ ] Close plan checklist when all tasks complete.
