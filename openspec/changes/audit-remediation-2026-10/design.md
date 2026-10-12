## Context

The audit in `docs/auditoria-codigo-2026-10.md` lists about 90 findings across four codebases. This design records the technical decisions needed to fix them consistently, so that each phase in `tasks.md` can be implemented and reviewed on its own.

Current state relevant to the decisions:
- `ApiService/Program.cs:66-92` configures JWT with `RoleClaimType = {ns}/roles` but calls `AddAuthorization()` with no policies. Endpoints only call `RequireAuthorization()`. Angular uses the role value `administrador` (`services/auth/user.service.ts:13`).
- `CurrentTenantService` is a singleton that reads `org_id` from `IHttpContextAccessor` and falls back to `"default"`. EF global query filters depend on it.
- `AiChatService` builds a Semantic Kernel per request with `new HttpClient(...)` and an always-true certificate callback. Its fallback reuses the tenant's key against OpenRouter.
- Two RabbitMQ consumers exist (`Infrastructure/Features/Ingestion/RabbitMqConsumerService.cs` and `Worker/Services/HierarchicalRabbitMqConsumerService.cs`), selected by `WORKER_PIPELINE_MODE`. Queues are declared in at least 3 places with `arguments: null`.
- The Go scraper keeps global mutable state (`providers`, `msgPublisher`, `scrapeMutex`), and its providers are mutated per request (`SetSections`, `UpdateFeeds`).

## Goals / Non-Goals

**Goals:**
- Enforce authorization and tenant isolation on the server, and test both.
- Make messaging and ingestion durable and idempotent.
- Restore Clean Architecture dependency direction so Application can be unit-tested without SDKs.
- Make CI the single quality gate for all three languages.
- Leave a folder and naming layout that new contributors can follow without guessing.

**Non-Goals:**
- Functional redesign of chat, arena or ingestion pipelines.
- Replacing Semantic Kernel, Qdrant, RabbitMQ or Auth0.
- Rewriting git history.

## Decisions

### D1. Authorization: named policies in a single place
- **Decision:** add `AuthorizationPolicies` (constants) and an `AddAppAuthorization()` extension in ApiService.
  - It defines `Policies.Admin` as `RequireRole("administrador")`, using the role claim that is already configured.
  - It defines `Policies.TenantMember` as an authenticated user who has a tenant.
- **Usage:** endpoint groups are created with `app.MapGroup("/api/ingestion").RequireAuthorization(Policies.Admin)`. Admin hub methods use `[Authorize(Policy = Policies.Admin)]`.
- **Angular:** role constants move to `core/auth/roles.ts`. The route guard stays as a UX convenience only.
- **Alternatives considered:**
  - `[Authorize(Roles=...)]` scattered per endpoint: easy to forget on new endpoints.
  - A fallback policy that requires Admin everywhere: too broad, because chat and profile are user-level.
- **gRPC `FilterConfigService`:** it is called by the Go scraper and is not user-facing. Restrict it with a shared-secret metadata header (`x-internal-key`, from configuration). It is mapped on the internal endpoint only, and is never routed through YARP.

### D2. Tenant resolution
- **Decision:** `CurrentTenantService` returns `null` when `org_id` is missing.
  - A `TenantRequiredMiddleware` (after `UseAuthorization`) returns 403 for authenticated requests without a tenant.
  - In Development, `Tenancy:DevelopmentFallbackTenant` (default `"default"`) is used instead.
- **Background work:** it receives the tenant explicitly. `ICurrentTenantService` gains `IDisposable BeginScope(string tenantId)`, backed by the existing `AsyncLocal` override, which is already the pattern the Worker uses. Fire-and-forget `Task.Run` blocks are replaced by a `Channel<BackgroundWorkItem>` plus a `BackgroundWorkQueue` hosted service, and each work item carries its `TenantId`.
- **`IgnoreQueryFilters()`:** any use must add an explicit `TenantId ==` predicate. This is enforced by an architecture test (see D9) that greps for `IgnoreQueryFilters` outside an allow-list.

### D3. Arena ownership
- **Decision:** add `TenantId` (required) and `UserId` (required) to `ArenaBattle`, with a global query filter on `TenantId`. `VoteAsync` and the hub `ArenaVote` load the battle with `SessionId == x && UserId == currentUser`; if no row matches, they return 404 and never 403, so that existence is not leaked.
- **Migration:** existing battles get `TenantId = 'legacy'` and `UserId = 'unknown'`, so analytics keep the history but no current user can vote them.
- **Analytics:** aggregation moves to SQL (`GroupBy`) and the result is cached per tenant for 60 s.

### D4. LLM client construction (`IChatModelFactory`)
- **Decision:** move kernel and chat-client creation from `AiChatService` to `Infrastructure/Ai/ChatModelFactory`.
  - It uses `IHttpClientFactory` named clients (`llm-google`, `llm-openai`, `llm-fallback`) with standard resilience.
  - TLS bypass is allowed only in Development, controlled by `Ai:Http:AllowInsecureTls` (default `false`).
- **Fallback provider:** configured through `Ai:Fallback:{Provider,Endpoint,ModelId,ApiKey}` (platform secret). When `ApiKey` is empty, the fallback is disabled and the original error is reported. The tenant key is never sent to a different provider.
- **Quota detection:** use `HttpOperationException.StatusCode == 429`, or the provider error code. Do not match on the message text.
- **Embeddings:** the embedding generator is always resolved from the injected platform kernel or the `IEmbeddingGenerator` service, never from the per-tenant chat kernel. This fixes the shadowed `kernel` bug.

### D5. Splitting `AiChatService`
Target components, all living in `Application/Features/Chat`:

| Component | Responsibility |
|---|---|
| `IChatModelFactory` (port, Infrastructure impl) | build `IChatCompletionService` per tenant configuration |
| `IRagPipeline` with `BaselineRagPipeline` and `HierarchicalRagPipeline` | retrieve context and build prompt messages |
| `PromptCatalog` (`Common/Prompts`) | every system and user prompt template (merges `Prompts.cs` and `SystemPrompts.cs`, removes inline prompts in `ChatHub` and `ArenaService`) |
| `ChatStreamingService` | stream with fallback, record usage |
| `ShadowTestScheduler` | enqueue the losing pipeline run to `BackgroundWorkQueue` (D2) with its own token |
| `ArenaStreamingService` | interleave Alfa and Beta with `Channel` reads, measure latency and tokens per side |
| `IAiCallRecorder` | single place for `AiCallLog` and metrics |

`AiChatService` stays as a thin facade that implements `IAiChatService`, so `ChatHub` does not change in the same PR. The `as AiChatService` service-locator casts are removed.

### D6. Messaging topology and the consumer base
- **Single source of truth:** `Infrastructure/Messaging/RabbitMqTopology.cs` declares, for each work queue `Q`:
  - `Q`, with `x-dead-letter-exchange = dlx` and `x-dead-letter-routing-key = Q.dlq`;
  - `Q.retry`, with a TTL of 30 s and dead-lettering back to `Q`;
  - `Q.dlq`.
- **Publishing:** every publisher uses `Persistent = true` and publisher confirms.
- **`RabbitMqConsumerBase<TMessage>`** handles connection, channel, QoS, reconnection on `ConnectionShutdown`, deserialization and ack/nack. It exposes `abstract Task HandleAsync(TMessage, IServiceProvider, CancellationToken)`.
  - On a transient exception the message is republished to `Q.retry`, and an `x-retry-count` header is incremented.
  - After `MaxRetries` (default 3), or on a poison message, it is nacked without requeue, so it reaches `Q.dlq`.
  - If the broker is unreachable at startup, the consumer retries forever with backoff and reports Unhealthy through a health check. It never exits silently.
- **Concrete consumers:** `BaselineIngestionConsumer` (Infrastructure) and `HierarchicalIngestionConsumer` (Infrastructure, moved out of Worker). Worker only selects which one to register.
- **Go publisher:** it must declare the same arguments. The queue names and arguments move to a shared `docs/messaging-contract.md`, and a Go constant mirrors it.
- **Migration:** existing queues must be deleted and recreated, because RabbitMQ rejects re-declaring a queue with different arguments. This is documented in tasks as a deploy step.

### D7. Ingestion consistency
- **Order of operations:**
  1. Parse and chunk.
  2. Embed (no side effects).
  3. In a database transaction: delete the old chunks and fragments for `DocumentId`, insert the new ones, update `DocumentJobState`, and commit.
  4. Upsert to Qdrant after a Qdrant `DeleteAsync(filter: documentId == X)`.
  5. If step 4 fails, mark `DocumentJobState = VectorSyncFailed` and let the retry path (D6) handle it.
- **Rationale:** a full outbox pattern is heavier than this project needs. Postgres is the source of truth, and Qdrant can be rebuilt from it, because the deletion is idempotent per document.
- **`ParentDocument`:** upserted by `(DocumentId, PipelineType)` with a unique index.
- **Admin bulk publish:** save `Queued` states first, then publish. A publish failure reverts the state to `Pending`.
- **`JobStatus`:** enum in Domain, stored as a string (no data change).

### D8. Realtime contract
- **Payload:** `DocumentStatusChanged` sends one object `{ documentId, status, errorMessage? }`. The DTO record lives in `Application/Common/Notifications`, and the Angular interface in `features/documents/models`.
- **Hub token:** query-string tokens are accepted for any path under `/hubs`.
- **Tenant scoping:** in `OnConnectedAsync`, each hub adds the connection to the group `tenant:{tenantId}`. Notifications use `Clients.Group(...)`.
  - `INotificationService` methods gain a `tenantId` parameter.
  - Ingestion events are global documents, so the server sends them to the admin group `role:admin` instead of every tenant.
- **Angular:** a single `HubConnectionFactory` with `accessTokenFactory: () => firstValueFrom(auth.getAccessTokenSilently())`.
  - `connect()` is idempotent (a promise is shared while a connection is in progress).
  - Hubs are stopped on logout.

### D9. Architecture and quality enforcement
- **Package versions:** `Directory.Packages.props` with central versions.
  - `Directory.Build.props` sets `Nullable=enable`, `TreatWarningsAsErrors=true` (initially only for `CS86xx` nullable warnings and `NU1903`) and `AnalysisLevel=latest-recommended`.
  - `Npgsql.EntityFrameworkCore.PostgreSQL` is pinned to a version compatible with EF Core 10, which removes the `NU1608` warning.
- **Architecture tests:** a new project `tests/AsistenteAyuntamiento.ArchitectureTests` uses NetArchTest and asserts that:
  - Domain references no NuGet package except BCL;
  - Application does not reference `Qdrant.*`, `Amazon.*`, `RabbitMQ.*`, `Npgsql*` or `Pgvector*`;
  - ApiService does not reference `Infrastructure.Data` directly in endpoints;
  - `IgnoreQueryFilters` is used only in allow-listed files.
- **CI jobs:** `dotnet` (build, test, format-check), `angular` (`npm ci`, lint, test headless, build), `go` (vet, test, build), `protos` (regenerate and run `git diff --exit-code`). Node version is 22 everywhere.
- **Deploy:** `needs: ci` (through `workflow_run` or a reusable workflow). Images are tagged with `sha-<short>` plus `latest`, and image names are normalized to lowercase with dashes.

### D10. Folder conventions

**.NET**
- `Features/<Feature>/{Contracts|DTOs}/`, one public type per file, namespace equals the folder path.
- Records for DTOs.
- English identifiers. User-facing strings stay in Spanish.
- `Infrastructure/Data/` replaces `Infrastructure/Infrastructure/Data/`.
  - Migrations are consolidated into `00000000000000_Baseline`. **Assumption to confirm:** no production database holds data that must survive. If one does, keep the history and instead add an idempotent script that marks the baseline as applied.
  - The embedded SQL resource names are updated accordingly.

**Angular**
```
src/app/
  core/      auth/ (auth-facade, roles, guards), realtime/ (hub factory), http/ (interceptors), layout/
  shared/    ui/, pipes/ (markdown)
  features/  chat/ documents/ settings/ profile/ arena/ analytics/ auth-pages/
             each with data-access/, models/, and component folders
```
- Components are `*.component.ts` with `OnPush`, `inject()`, `input()`/`output()`/`viewChild()`, `@if`/`@for` and lazy `loadComponent`.

**Go**
```
cmd/scraper/main.go             wiring only
internal/config                 env loading and validation
internal/httpx                  shared client (timeout, rate limit, retry with ctx, body close)
internal/provider/{boe,boja}    stateless; FetchSummary(ctx, date, Options)
internal/pipeline               Orchestrator{storage, publisher, filters, providers}, job lock
internal/messaging              publisher with confirms and reconnect
internal/protos                 generated via go:generate
```

## Risks / Trade-offs

- **[Breaking auth]:** non-admin users who currently use the admin screens lose access. → Mitigation: audit Auth0 role assignments before deploying phase 1, and communicate the change.
- **[403 for users without an organization]:** → Mitigation: a Development fallback stays, and production users must belong to an organization (this was already the B2B intent).
- **[Queue re-declaration]:** deleting queues loses the messages in flight. → Mitigation: drain the queues (stop the scraper, wait until queue depth is 0) before deploying phase 3.
- **[Migration baseline]:** existing databases need `__EFMigrationsHistory` reset. → Mitigation: a scripted procedure in `docs/setup.md`, plus dropping and recreating the dev databases.
- **[Large refactor of `AiChatService`]:** risk of regressions in streaming and arena. → Mitigation: phase 4 adds characterization tests first (streaming fallback, arena interleaving) with fake `IChatCompletionService`. The facade keeps the public contract.
- **[`TreatWarningsAsErrors`]:** may block unrelated PRs. → Mitigation: enable it per warning category, after phase 2 fixes the current 25 warnings.

## Migration Plan

1. Phase 1 → deploy → verify the 403 and 401 matrix with integration tests and a smoke test.
2. Phase 2 → deploy.
3. Drain the queues → phase 3 → recreate the queues → deploy the Go scraper and the .NET workers together.
4. Phases 4–7 have no runtime contract changes except the baseline migration (phase 5), which needs the documented history reset.
- **Rollback:** each phase is a separate PR and image tag (`sha-*`), so redeploy the previous tag. Phase 3 rollback requires recreating the queues without DLX arguments.

## Open Questions

- Is there a production database with data that must be preserved? This decides between the migration baseline and keeping the history (D10).
- Should the `administrador` role be split into `platform-admin` (global ingestion and scraper) and `tenant-admin` (AI config of the tenant)? Phase 1 uses one role; the split is a follow-up if needed.
- Arena analytics across tenants: should platform admins see global win rates? The default here is per tenant, plus a global view for `administrador`.
- Should the `user-api-keys-vault` spec be rewritten to match the DataProtection implementation, or should Infisical be implemented? This is out of scope here and is recorded as a follow-up.
