## Why

A full code audit (`docs/auditoria-codigo-2026-10.md`) of the .NET backend, Angular SPA, Go scraper and CI found confirmed defects that compromise security, tenant isolation and data integrity, plus structural debt that makes further evolution risky:

- **Authorization is client-side only.** No API endpoint enforces a role, so any authenticated user can reset the vector store, reprocess all of S3, change the tenant's LLM API key or trigger the scraper.
- **Tenant isolation leaks.** Users without `org_id` share a `"default"` tenant, `ArenaBattle` has no owner (IDOR), SignalR broadcasts to every tenant, background tasks lose the tenant, and AI metrics mix tenants.
- **Credential and transport exposure.** The LLM quota fallback sends the tenant's Google/OpenAI key to OpenRouter, TLS validation is disabled unconditionally in `AiChatService`, and the Go filter client uses `InsecureSkipVerify`.
- **Functional bugs.**
  - The Go keyword filter always matches (`|| true`).
  - `GetCompletionAsync` resolves the embedding generator from a shadowed local kernel.
  - Document status notifications never reach the UI (payload mismatch, and the hub returns 401 over WebSockets).
  - RabbitMQ messages are transient and failed ones are silently dropped.
  - The filter gRPC fails open, so the scraper downloads everything.
  - Re-ingestion leaves orphan vectors in Qdrant.
- **Structural debt.**
  - The Application and Domain layers depend on Qdrant, S3, RabbitMQ and Npgsql.
  - `AiChatService` is a ~1100-line god class.
  - The two RabbitMQ consumers are ~90% duplicated.
  - Package versions drift across projects.
  - A 34 MB binary is committed to git.
  - The Angular specs do not compile, Go has no tests, and CI does not build Go or run the frontend tests.

The project should be safe and well organized before further features are built on it.

## What Changes

The work is delivered in **phases**. Each phase is shippable on its own and maps to one PR.

1. **Security and tenant hardening (BREAKING for clients that relied on the defaults):**
   - Role policies on every admin endpoint, plus a role guard on the `arena` route.
   - Requests without `org_id` are rejected outside Development.
   - `ArenaBattle` gets an owner (`TenantId` + `UserId`).
   - SignalR broadcasts are scoped to tenant groups.
   - Exception details are no longer returned to clients.
   - The app refuses to start when a required secret is missing.
2. **Critical functional bugs:**
   - LLM fallback uses its own credentials, and TLS bypasses are removed.
   - The shadowed kernel bug is fixed.
   - The SignalR payload contract is fixed, hub query-token authentication works, and the Angular token factory refreshes the token.
   - Go keyword matching is fixed and the filter gRPC fails closed.
   - `await ?.` null crashes are fixed.
   - The hierarchical workers use the configured bucket.
3. **Messaging and ingestion reliability:**
   - Persistent messages, dead-letter exchanges and bounded retries.
   - A single `RabbitMqConsumerBase<T>` with reconnection.
   - Publish after commit.
   - Idempotent re-ingestion that deletes stale Qdrant points.
4. **Repository hygiene and quality gates:**
   - Remove the committed binary and stale artifacts, fix `.gitignore`, and unify the duplicated skills folders.
   - Add `Directory.Packages.props` and align package versions.
   - CI builds and tests .NET, Angular and Go, and deploy is gated on CI with immutable image tags.
   - Fix the Angular specs and add a characterization test baseline.
5. **Clean Architecture boundaries:**
   - Introduce ports (`IVectorStore`, `IBlobStorage`, `IMessagePublisher`, `IFullTextSearch`, `IChatModelFactory`) and move the SDKs to Infrastructure.
   - Split `AiChatService`.
   - Add `AddApplicationServices()`.
   - Flatten `Infrastructure/Infrastructure/Data` and consolidate migrations into a baseline.
6. **Go scraper restructuring:**
   - `internal/config`, a shared HTTP client, and stateless providers.
   - An orchestrator with dependency injection, graceful shutdown and `slog`.
7. **Angular restructuring:**
   - `AuthFacade`, a SignalR hub factory, `ChatStore` and `MarkdownService`.
   - A `core/shared/features` layout.
   - Fixes for subscription leaks, migration of the control flow syntax, `OnPush`, and basic accessibility.

## Capabilities

### New Capabilities
- `api-authorization`: role-based authorization policies on the server for administrative APIs and hubs, mirrored by the Angular route guards.
- `llm-provider-security`: how LLM provider clients are built (credential scoping, fallback provider credentials, TLS, `HttpClient` lifetime).
- `realtime-notifications`: the SignalR notification contract, hub authentication, tenant-scoped delivery and token refresh.
- `messaging-reliability`: durable RabbitMQ publishing, dead-lettering, retries and consumer lifecycle.
- `ingestion-consistency`: idempotent ingestion and consistency between Postgres, Qdrant and RabbitMQ.
- `clean-architecture-boundaries`: dependency rules between Domain, Application, Infrastructure and the hosts.
- `build-quality-gates`: CI coverage, central package management, repository hygiene and minimum test baseline.

### Modified Capabilities
- `multi-tenant-user-profiles`: removes the production `"default"` tenant fallback and requires explicit tenant propagation to background work and to queries that ignore filters.
- `question-arena`: arena battles become owned and tenant-scoped, and the stale "unauthenticated access" requirement is removed (the route is already authenticated).
- `scraper-dynamic-filtering`: the filter gRPC fails closed, keyword matching is defined, scrape options are passed per call, and scrape execution is serialized.

## Impact

- **Backend .NET:**
  - `ApiService/Program.cs` and all `Features/*Endpoints.cs`, `ChatHub.cs`, `NotificationHub.cs`, `SignalRNotificationService.cs` and `CurrentTenantService.cs`.
  - `Application/Features/Chat/AiChatService.cs`, `Features/Ingestion/*`, `Features/Arena/*` and `Features/Users/*`.
  - `Infrastructure/DependencyInjection.cs`, the RabbitMQ consumers and the `Data/` folder with its migrations.
  - `Worker/Program.cs` and `Worker/Services/*`.
- **Domain:** `ArenaBattle` (new columns `TenantId`, `UserId`), `ChildFragment` (shadow `tsvector`), and a new `JobStatus` enum. **These need a database migration.**
- **Angular:** `app.routes.ts`, `services/chat/chat.service.ts`, `services/core/notification.service.ts`, `pages/*`, all specs, and the folder layout.
- **Go:** `main.go`, `internal/*`, `Dockerfile`, `.gitignore`; delete `protos/`, `cmd/validator` and `scrape-history.ps1`.
- **Ops:** `.github/workflows/ci.yml`, `.github/workflows/deploy.yml`, `docker-compose.yml`, `.env.example`, `Directory.Build.props`, and a new `Directory.Packages.props`.
- **Breaking:**
  - Users without an Auth0 Organization receive 403 in production.
  - Non-admin users lose access to the admin APIs.
  - The RabbitMQ queues must be re-declared with DLX arguments: delete and recreate them, because the arguments of an existing queue cannot be changed.

## Non-goals

- No new end-user features. Behaviour changes only where the current behaviour is a defect or a security gap.
- No change of providers (Auth0, Qdrant, R2, RabbitMQ, Semantic Kernel stay).
- Implementing the Infisical vault described in `user-api-keys-vault` is out of scope. The drift between that spec and the actual `AiConfiguration` + DataProtection implementation is only recorded as a follow-up.
- No BOPMA implementation. It is removed from the active provider registry until a separate change implements it.
- No rewrite of git history to purge the committed binary. That is a team decision, documented as an optional step.
- No full Angular redesign. Only the structural refactor and the targeted accessibility fixes.
