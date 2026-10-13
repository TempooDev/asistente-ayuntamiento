# clean-architecture-boundaries Specification

## Purpose
Keep Domain and Application free of infrastructure SDKs: Application defines ports, Infrastructure implements them, and architecture tests enforce the dependency rules.
## Requirements
### Requirement: Dependency Direction Between Layers
The .NET solution SHALL respect the dependency rule `Domain ← Application ← Infrastructure ← Hosts (ApiService, Worker)`, and automated architecture tests SHALL enforce it:
- Domain SHALL NOT reference any persistence, vector or messaging package (`Npgsql*`, `Pgvector*`, `Microsoft.EntityFrameworkCore*`).
- Application SHALL NOT reference `Qdrant.*`, `Amazon.*`, `RabbitMQ.*`, `Npgsql*`, `Pgvector*`, or provider-specific Semantic Kernel connectors.

#### Scenario: Architecture test run
- **WHEN** the test suite runs in CI
- **THEN** `AsistenteAyuntamiento.ArchitectureTests` SHALL fail if any forbidden reference exists in Domain or Application

#### Scenario: Developer adds a forbidden package
- **WHEN** a developer adds `Qdrant.Client` to `AsistenteAyuntamiento.Application.csproj`
- **THEN** the CI build SHALL fail

### Requirement: Ports for External Systems
The Application layer SHALL depend on these abstractions, implemented in Infrastructure:
- `IVectorStore` (upsert, delete by document, search);
- `IBlobStorage` (list, get, exists);
- `IMessagePublisher` (publish with confirm);
- `IFullTextSearch` (lexical search);
- `IChatModelFactory` (chat completion per tenant configuration).

`IAppDbContext` SHALL NOT expose `DatabaseFacade`, and SHALL NOT be cast to `DbContext`.

#### Scenario: Unit-testing hybrid retrieval
- **WHEN** a unit test exercises `HybridRetrievalService` with in-memory fakes of `IVectorStore` and `IFullTextSearch`
- **THEN** the test SHALL run without Postgres, Qdrant or network access

### Requirement: Single-Responsibility Chat Services
Chat orchestration SHALL be split into these components:
- a model factory;
- RAG pipelines;
- a prompt catalog;
- a streaming service with fallback;
- a shadow-test scheduler;
- an arena streaming service;
- an AI call recorder.

No class in `Application/Features/Chat` SHALL exceed 400 lines, or depend on more than 7 injected services. Services SHALL NOT resolve themselves from `IServiceProvider` or cast interfaces to concrete types.

#### Scenario: Static size check
- **WHEN** the architecture tests run
- **THEN** they SHALL report any class in `Application/Features/Chat` above the configured limits

### Requirement: No Untracked Fire-and-Forget Work
Application and host code SHALL NOT start untracked background work with `Task.Run` or discarded tasks (`_ = ...`). Background work SHALL be enqueued to `BackgroundWorkQueue`, which observes exceptions, logs them, carries the tenant and honours host shutdown.

#### Scenario: Shadow test during shutdown
- **WHEN** the host begins graceful shutdown while shadow-test items are queued
- **THEN** in-progress items SHALL receive cancellation
- **AND** their failures SHALL be logged, not lost

### Requirement: Business Logic Outside Endpoints and Hubs
Minimal API endpoints, SignalR hubs and gRPC services SHALL only map transport to application services. Aggregations, persistence and prompt construction SHALL live in Application services.

#### Scenario: AI metrics endpoint
- **WHEN** `GET /api/ai/metrics` is called
- **THEN** the endpoint SHALL delegate to an application service that aggregates in the database, scoped to the tenant
- **AND** the endpoint SHALL NOT materialize whole tables with `ToListAsync()`

### Requirement: Consistent Project Layout and Conventions
The codebase SHALL follow the conventions defined in the design (D10):
- one public type per file;
- namespaces equal to folder paths;
- DTOs as records under `Features/<Feature>/DTOs`;
- English identifiers;
- structured logging templates (no interpolated log messages, no `Console.WriteLine`);
- `CancellationToken` on every public async API;
- the folder `Infrastructure/Data` (not `Infrastructure/Infrastructure/Data`).

#### Scenario: Formatting and analyzers in CI
- **WHEN** CI runs `dotnet format --verify-no-changes` and the analyzers
- **THEN** violations of `.editorconfig` naming, logging (`CA2254`) and cancellation (`CA2016`) rules SHALL fail the build

