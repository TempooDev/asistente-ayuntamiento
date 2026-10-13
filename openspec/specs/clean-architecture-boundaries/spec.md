# clean-architecture-boundaries Specification

## Purpose
Keep Domain and Application free of infrastructure SDKs: Application defines ports, Infrastructure implements them, transport code only maps requests to Application services, and architecture tests (`tests/AsistenteAyuntamiento.ArchitectureTests`) enforce the rules that can be checked automatically.

## Requirements
### Requirement: Dependency Direction Between Layers
The .NET solution SHALL respect the dependency rule `Domain ← Application ← Infrastructure ← Hosts (ApiService, Worker)`, enforced by `LayerDependencyTests`:
- Domain types SHALL NOT depend on `Qdrant`, `Amazon`, `RabbitMQ`, `Npgsql`, `NpgsqlTypes`, `Pgvector`, `Microsoft.SemanticKernel.Connectors`, `OllamaSharp`, `Microsoft.EntityFrameworkCore`, `AsistenteAyuntamiento.Infrastructure` or `AsistenteAyuntamiento.Application`, and the Domain project SHALL reference no package.
- Application types SHALL NOT depend on the same SDK namespaces or on `AsistenteAyuntamiento.Infrastructure`, and the Application project SHALL only reference `Microsoft.AspNetCore.DataProtection.Abstractions`, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.SemanticKernel`.

#### Scenario: Developer adds a forbidden package
- **WHEN** a developer adds `Qdrant.Client` to `AsistenteAyuntamiento.Application.csproj`
- **THEN** `Project_only_references_allowed_packages` SHALL fail in CI

#### Scenario: Rule control
- **WHEN** the architecture tests run
- **THEN** the same dependency rule applied to Infrastructure SHALL report `QdrantVectorStore`, proving the rule detects SDK usage

### Requirement: Ports for External Systems
The Application layer SHALL depend on these abstractions, implemented in Infrastructure:
- `IVectorStore` (ensure/drop collection, upsert, delete by document, search);
- `IBlobStorage` (ensure container, list, open read, exists);
- `IIngestionQueuePublisher` (publish an ingestion message to the selected pipelines; returns only after broker confirmation);
- `IFullTextSearch` (lexical search over child fragments);
- `IChatModelFactory` (chat completion service per tenant configuration, plus the platform fallback model);
- `IBackgroundWorkQueue` (tracked background work).

`IAppDbContext` SHALL expose entity sets, `ExecuteInTransactionAsync` and `SaveChangesAsync`, and SHALL NOT expose `DatabaseFacade`.

#### Scenario: Unit-testing hybrid retrieval
- **WHEN** a unit test exercises `HybridRetrievalService` with fakes of `IVectorStore` and `IFullTextSearch`
- **THEN** the test SHALL run without Postgres, Qdrant or network access

### Requirement: Single-Responsibility Chat Services
Chat orchestration SHALL be split into: a model factory (`IChatModelFactory`), RAG pipelines (`Pipelines/`), a prompt catalog (`Common/Prompts/PromptCatalog`), a streaming service with fallback (`ChatStreamingService`), a shadow-test scheduler, an arena streaming service and an AI call recorder. `ChatServiceRulesTests` SHALL enforce, for `Application/Features/Chat`:
- no source file longer than 400 lines;
- no public constructor of a non-record class with more than 7 parameters;
- no cast of the form `as <Name>Service`.

#### Scenario: Oversized chat file
- **WHEN** a file under `src/AsistenteAyuntamiento.Application/Features/Chat/` exceeds 400 lines
- **THEN** `Chat_files_stay_under_the_size_limit` SHALL fail

### Requirement: No Untracked Fire-and-Forget Work
Application and ApiService code SHALL NOT start untracked background work with `Task.Run(` or discarded tasks (`_ = Task.…` or `_ = …Async(`); `ChatServiceRulesTests.No_untracked_fire_and_forget_work` SHALL enforce it for those two projects. Background work SHALL be enqueued to `IBackgroundWorkQueue` (bounded to 200 items), whose processor runs each item in its own DI scope under its tenant, logs failures, passes the host shutdown token and awaits in-flight items on stop.

#### Scenario: Shadow test during shutdown
- **WHEN** the host stops while shadow-test items are running
- **THEN** those items SHALL receive cancellation
- **AND** their cancellation or failure SHALL be logged, not lost

#### Scenario: Queue full
- **WHEN** work is enqueued while the queue holds 200 items
- **THEN** `TryEnqueue` SHALL return `false`
- **AND** `POST /api/users/me/preferences/analyze` and `POST /api/scraper/filters/trigger` SHALL answer `503`, while the shadow-test scheduler SHALL log a warning and skip the test

### Requirement: Business Logic Outside Endpoints and Hubs
Minimal API endpoints, SignalR hubs and gRPC services SHALL only map transport to Application services. `TransportLayerTests` SHALL fail when a file under `src/AsistenteAyuntamiento.ApiService/Features/` references `IAppDbContext`, `AppDbContext` or `Microsoft.EntityFrameworkCore`, and when any ApiService file sets a system prompt (`new ChatHistory("` or `AddSystemMessage(`); system prompts belong to the Application pipelines.

#### Scenario: Tenant AI metrics
- **WHEN** an organization administrator calls `GET /api/ai/metrics/summary` or `GET /api/ai/metrics/history`
- **THEN** the endpoint SHALL delegate to `IAiMetricsQueryService`, which aggregates the tenant's persisted calls in the database

#### Scenario: Cross-tenant in-memory metrics
- **WHEN** a platform administrator calls `GET /api/ai/metrics`
- **THEN** the endpoint SHALL return the in-memory snapshot of `IAiMetricsService` without querying the database

#### Scenario: Hub adds a system message
- **WHEN** a developer calls `AddSystemMessage(` in `ChatHub`
- **THEN** `ApiService_does_not_set_system_prompts` SHALL fail

### Requirement: Consistent Formatting and Conventions
The codebase SHALL follow `.editorconfig`: file-scoped namespaces, `using` directives outside the namespace, interfaces prefixed `I`, PascalCase types and members, `_camelCase` private fields, constant structured-logging templates (`CA2254`) and forwarded cancellation tokens (`CA2016`). CI SHALL fail only on whitespace formatting (`dotnet format whitespace --verify-no-changes`); the naming and analyzer rules are reported as warnings.

#### Scenario: Whitespace drift
- **WHEN** a pull request contains code whose whitespace differs from `.editorconfig`
- **THEN** the CI `dotnet` job SHALL fail at "Verify formatting"

#### Scenario: Interpolated log message
- **WHEN** code logs an interpolated string
- **THEN** the build SHALL report a `CA2254` warning without failing
