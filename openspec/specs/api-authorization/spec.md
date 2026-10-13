# api-authorization Specification

## Purpose
Enforce on the server, not only in the Angular guards, that administrative APIs require the right administrator role (platform or organization), that internal gRPC services require a shared key, that clients never receive internal error details, and that the services refuse to start without the secrets they need.

## Requirements
### Requirement: Server-Side Role Enforcement for Administrative APIs
The ApiService SHALL enforce two administrator scopes through named authorization policies registered once in `AddAppAuthorization()` (`Security/AuthorizationPolicies.cs`). Roles are read from the claim `{Auth0:CustomClaimsNamespace}/roles` and compared case-insensitively. Client-side route guards SHALL be treated as UX only.

- **Platform administrators** (role `admin-plataforma`, policy `Policies.PlatformAdmin`): `/api/ingestion/**`, `/api/scraper/filters/**`, `GET /api/ai/metrics` and `GET /api/ai/health` (in-memory snapshot of every tenant's calls), `GET /api/arena/analytics` (aggregates across tenants).
- **Organization administrators** (role `admin-organizacion`, policy `Policies.OrganizationAdmin`): `GET|PUT /api/settings/ai`, `GET /api/ai/metrics/summary`, `GET /api/ai/metrics/history`, `POST /api/arena/compare`.
- The retired role `administrador` SHALL NOT grant any administrative access.
- `POST /api/arena/vote` and `/api/users/**` SHALL only require an authenticated user; `GET /api/config/auth0` SHALL be anonymous (public Auth0 client settings for the SPA).

#### Scenario: Platform administrator calls a platform endpoint
- **GIVEN** a user whose token contains the role `admin-plataforma` and an `org_id`
- **WHEN** they call `GET /api/scraper/filters`
- **THEN** the API SHALL respond `200 OK`

#### Scenario: Administrator of the other scope
- **WHEN** an organization administrator calls a platform endpoint, or a platform administrator calls an organization endpoint
- **THEN** the API SHALL respond `403 Forbidden`

#### Scenario: Retired role
- **GIVEN** a user whose token contains only the role `administrador`
- **WHEN** they call `GET /api/scraper/filters` or `GET /api/settings/ai`
- **THEN** the API SHALL respond `403 Forbidden`

#### Scenario: Authenticated non-admin calls an admin endpoint
- **GIVEN** a user with a valid token, an `org_id` and no administrator role
- **WHEN** they call any administrative endpoint (e.g. `PUT /api/settings/ai`, `POST /api/scraper/filters/trigger`)
- **THEN** the API SHALL respond `403 Forbidden` without executing the handler

#### Scenario: Unauthenticated call to an admin endpoint
- **WHEN** a request without a bearer token calls any administrative endpoint
- **THEN** the API SHALL respond `401 Unauthorized`

#### Scenario: Role casing
- **WHEN** the roles claim contains `Admin-Plataforma`
- **THEN** the server SHALL treat it as `admin-plataforma`, like the SPA guard

### Requirement: Policies Applied per Route Group or per Endpoint, Verified by Enumeration
Policies SHALL be applied at route-group level where a prefix belongs to a single scope (`/api/ingestion`, `/api/scraper/filters` → `PlatformAdmin`; `/api/settings` → `OrganizationAdmin`). Where a prefix mixes scopes, the policy SHALL be set per endpoint: `/api/ai` uses one group per policy, and `/api/arena` sets the policy on `compare` and `analytics` only. The integration test `AdminAuthorizationTests.Every_endpoint_under_an_admin_prefix_requires_its_admin_policy` SHALL enumerate every mapped route (`EndpointDataSource`) under the admin areas `/api/ingestion`, `/api/scraper`, `/api/settings`, `/api/ai` and `/api/arena`, and fail when a route lacks its expected authorization, allows anonymous access, or is not classified. The classification table has prefix rules for the single-scope areas and for `/api/ai` (`PlatformAdmin` by default), and exact rules that win over prefixes: `/api/ai/metrics/summary` and `/api/ai/metrics/history` → `OrganizationAdmin`, `/api/arena/analytics` → `PlatformAdmin`, `/api/arena/compare` → `OrganizationAdmin`, `/api/arena/vote` → authenticated user. `/api/arena` has no prefix rule, so every route there must be listed explicitly. The test SHALL also fail when an exact rule no longer matches a mapped route.

#### Scenario: New endpoint in a single-scope group
- **WHEN** a developer maps a new endpoint inside the `/api/ingestion` group
- **THEN** it SHALL inherit `PlatformAdmin` without a per-endpoint attribute
- **AND** the enumeration test SHALL pass

#### Scenario: New endpoint under /api/ai without the expected policy
- **WHEN** a developer maps `/api/ai/new-thing` in the organization group
- **THEN** the enumeration test SHALL fail, because every `/api/ai/` route not listed explicitly is expected to require `PlatformAdmin`

#### Scenario: New endpoint under /api/arena
- **WHEN** a developer maps a new route under `/api/arena` other than `compare`, `analytics` and `vote`
- **THEN** the enumeration test SHALL fail, reporting the route as not classified, until it is added to the table with its expected authorization
- **AND** once added, the test SHALL fail if the route does not require that policy

### Requirement: Internal gRPC Service Protection
The ApiService `FilterConfigService` SHALL only accept calls that present the configured `Internal:GrpcApiKey` in the `x-internal-key` metadata header, compared in constant time, and SHALL NOT be routed through the public gateway (the gateway only proxies `/api`, `/openapi` and `/hubs`). The ApiService SHALL send the same key on its calls to the Go scraper's `ScraperCommandService`, which SHALL require it.

#### Scenario: Go scraper requests filters with a valid key
- **WHEN** the Go scraper calls `GetActiveFilters` with the correct `x-internal-key`
- **THEN** the service SHALL return the active filter rules

#### Scenario: Call without or with a wrong internal key
- **WHEN** a client calls `GetActiveFilters` without `x-internal-key`, or with a wrong one
- **THEN** the service SHALL fail with gRPC status `Unauthenticated` and return no rule

#### Scenario: Key not configured
- **GIVEN** `Internal:GrpcApiKey` is empty
- **WHEN** a client calls `GetActiveFilters`
- **THEN** the call SHALL be accepted with a warning in Development and rejected with `Unauthenticated` in any other environment

### Requirement: Client Route Guards Mirror Server Policies
The Angular router SHALL apply `authGuard` and `roleGuard` to every route whose backing API requires an admin policy, using the role constants of `core/auth/roles.ts`:
- `/documentos` requires a platform administrator;
- `/arena` requires an organization administrator;
- `/configuracion` and `/admin/analytics` require either role and show only the sections of the user's scope (AI settings and usage for organization administrators; scraper filters and Arena results for platform administrators).

#### Scenario: Non-admin navigates to an admin route
- **GIVEN** an authenticated user without an administrator role
- **WHEN** they navigate to `/configuracion`, `/documentos`, `/arena` or `/admin/analytics`
- **THEN** the router SHALL redirect them to `/chat`
- **AND** the page SHALL NOT be loaded

### Requirement: No Internal Error Details Returned to Clients
HTTP endpoints and SignalR hub methods SHALL NOT return exception type names, messages or stack traces to clients; details SHALL only be written to the server logs.
- HTTP: unhandled exceptions SHALL go through `UseExceptionHandler()` with `AddProblemDetails()`, returning a `500` RFC 7807 `ProblemDetails` that carries a `traceId`.
- Hubs: errors SHALL be plain text with a stable code, `Error [UNAUTHORIZED|NOT_FOUND|INVALID_REQUEST|INTERNAL]: …` (`HubErrors`), sent as a stream item, a `ReceiveMessage` payload or a `HubException` message.
- LLM failures SHALL be text prefixed `[LLM_QUOTA_EXCEEDED]`, `[LLM_NOT_CONFIGURED]` or `[LLM_UNAVAILABLE]` (`LlmErrorCodes`).
- The SPA SHALL map these codes to user messages (`stream-errors.ts`) and treat any other failure as a lost connection.

#### Scenario: Unhandled exception in an endpoint
- **WHEN** an endpoint throws an unhandled exception outside Development
- **THEN** the response SHALL be `500` with a generic `ProblemDetails` containing a `traceId`

#### Scenario: Failure in the middle of a chat stream
- **WHEN** the model stream throws after the hub method returned
- **THEN** the hub SHALL log the exception and end the stream with a `HubException` whose message is `Error [INTERNAL]: …`
- **AND** SHALL NOT include `ex.Message` or the exception type

#### Scenario: Model quota exhausted before any content
- **GIVEN** the tenant's model fails with a quota error and no fallback model is configured
- **WHEN** the user streams a message
- **THEN** the stream SHALL yield the text `[LLM_QUOTA_EXCEEDED] …` and end normally

### Requirement: Fail Fast on Missing Secrets
The ApiService SHALL validate configuration at startup (`RequiredConfiguration.Validate`) and refuse to start when a required key is missing:
- in every environment: `Auth0:Domain`, `Auth0:Audience`;
- in Production only: `Auth0:CustomClaimsNamespace`, `Blob:Endpoint`, `Blob:AccessKeyId`, `Blob:SecretAccessKey`, `Internal:GrpcApiKey`.

The shared Infrastructure registration (ApiService and Worker) SHALL also fail when `Blob:Endpoint` is set without its access keys, and, in Production only, when `ConnectionStrings:qdrant` has no `Key=`. In `docker-compose.yml`, `QDRANT_API_KEY`, `RABBITMQ_PASS` and `INTERNAL_GRPC_KEY` SHALL be mandatory (`${VAR:?}`), and `.env.example` SHALL contain no secret values. The Go scraper SHALL refuse to start when `INTERNAL_GRPC_KEY` is missing.

#### Scenario: Missing blob credentials in production
- **GIVEN** `ASPNETCORE_ENVIRONMENT=Production` and an empty `Blob:SecretAccessKey`
- **WHEN** the ApiService starts
- **THEN** startup SHALL fail with an `InvalidOperationException` naming the missing key

#### Scenario: Missing compose secret
- **WHEN** `docker compose up` runs without `INTERNAL_GRPC_KEY` in the environment
- **THEN** compose SHALL refuse to start with the message `INTERNAL_GRPC_KEY is required`

#### Scenario: Local development secrets
- **WHEN** the AppHost runs locally
- **THEN** the MinIO credentials and the internal gRPC key SHALL be Aspire parameters generated and persisted in user secrets, not hardcoded literals
- **AND** the local MinIO bucket SHALL be created without anonymous access
