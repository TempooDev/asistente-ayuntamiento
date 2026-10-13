# api-authorization Specification

## Purpose
Enforce on the server, not only in the Angular guards, that administrative APIs require the right administrator role (platform or organization) and that every request runs inside a tenant.
## Requirements
### Requirement: Server-Side Role Enforcement for Administrative APIs
The API SHALL enforce, through named authorization policies on the server, two administrator scopes (roles in the claim `{CustomClaimsNamespace}/roles`). Client-side route guards SHALL be treated as UX only and SHALL NOT be relied upon for security.

- **Platform administrators** (`admin-plataforma`, policy `PlatformAdmin`) manage the shared knowledge base and data that spans tenants:
  - `/api/ingestion/**`;
  - `/api/scraper/**`;
  - `/api/ai/metrics` and `/api/ai/health` (in-memory snapshot of every tenant's calls);
  - `/api/arena/analytics` (aggregates across tenants).
- **Organization administrators** (`admin-organizacion`, policy `OrganizationAdmin`) manage their own tenant:
  - `/api/settings/ai`;
  - `/api/ai/metrics/summary` and `/api/ai/metrics/history`;
  - `/api/arena/compare`.
- The role from before the split, `administrador`, SHALL be accepted by both policies until it is no longer assigned in Auth0.

#### Scenario: Platform administrator calls a platform endpoint
- **GIVEN** a user whose JWT contains the role `admin-plataforma` and a valid `org_id`
- **WHEN** they call `POST /api/ingestion/reset`
- **THEN** the API SHALL authorize the request and execute the operation

#### Scenario: Administrator of the other scope
- **WHEN** an organization administrator calls a platform endpoint, or a platform administrator calls an organization endpoint
- **THEN** the API SHALL respond `403 Forbidden`

#### Scenario: Legacy role during the transition
- **GIVEN** a user with the role `administrador`
- **WHEN** they call a platform or an organization endpoint
- **THEN** the API SHALL authorize the request

#### Scenario: Authenticated non-admin calls an admin endpoint
- **GIVEN** a user with a valid JWT but without an administrator role
- **WHEN** they call any administrative endpoint (e.g. `PUT /api/settings/ai`, `POST /api/scraper/filters/trigger`)
- **THEN** the API SHALL respond `403 Forbidden`
- **AND** SHALL NOT execute any side effect

#### Scenario: Unauthenticated call to an admin endpoint
- **WHEN** a request without a bearer token calls any administrative endpoint
- **THEN** the API SHALL respond `401 Unauthorized`

#### Scenario: Role casing
- **WHEN** the roles claim contains `Admin-Plataforma` (different casing)
- **THEN** the server SHALL treat it as `admin-plataforma`, exactly like the SPA guard, which lowercases roles

### Requirement: Policies Declared Once and Applied per Route Group
Authorization policies SHALL be defined in a single registration (`AddAppAuthorization()`) and applied at the route group level, so that new endpoints added to an admin group inherit the policy automatically.

#### Scenario: Adding a new endpoint to an admin group
- **WHEN** a developer maps a new endpoint inside the `/api/ingestion` route group
- **THEN** the endpoint SHALL require the group's policy (`PlatformAdmin` for ingestion) without any per-endpoint attribute
- **AND** an integration test SHALL enumerate all mapped endpoints and fail if an endpoint under an admin prefix does not require its expected policy

### Requirement: Internal gRPC Service Protection
The `FilterConfigService` gRPC service SHALL only accept calls that present the configured internal shared secret in the `x-internal-key` metadata header, and SHALL NOT be routed through the public gateway.

#### Scenario: Go scraper requests filters with a valid key
- **WHEN** the Go scraper calls `GetActiveFilters` with the correct `x-internal-key`
- **THEN** the service SHALL return the active filter rules

#### Scenario: Call without or with a wrong internal key
- **WHEN** a client calls `GetActiveFilters` without `x-internal-key`, or with a wrong one
- **THEN** the service SHALL fail with gRPC status `Unauthenticated`
- **AND** SHALL NOT return any rule

### Requirement: Client Route Guards Mirror Server Policies
The Angular router SHALL apply the role guard to every route whose backing API requires an admin policy, using role constants from a single `core/auth/roles.ts`:
- `/documentos` requires a platform administrator;
- `/arena` requires an organization administrator;
- `/configuracion` and `/admin/analytics` require either, and show only the sections of the user's scope (AI settings and usage for organization administrators; scraper filters and Arena results for platform administrators).

#### Scenario: Non-admin navigates to an admin route
- **GIVEN** an authenticated user without an administrator role
- **WHEN** they navigate to `/configuracion`, `/documentos` or `/admin/analytics`
- **THEN** the router SHALL redirect them to the chat page
- **AND** no administrative API call SHALL be issued

### Requirement: No Internal Error Details Returned to Clients
HTTP endpoints and SignalR hub methods SHALL NOT return exception type names, messages or stack traces to clients. Errors SHALL be returned as RFC 7807 `ProblemDetails` with a correlation `traceId`, or, in hubs, as a typed error code.

#### Scenario: Unhandled exception in an endpoint
- **WHEN** an endpoint throws an unhandled exception
- **THEN** the response SHALL be `500` with a generic `ProblemDetails` that contains a `traceId`
- **AND** the exception details SHALL only be written to the server logs

#### Scenario: Error during chat streaming
- **WHEN** the chat stream fails
- **THEN** the hub SHALL send a typed error (e.g. `{ code: "LLM_QUOTA_EXCEEDED" | "LLM_UNAVAILABLE" | "INTERNAL" }`)
- **AND** SHALL NOT include `ex.Message` or `ex.GetType().Name`

### Requirement: Fail Fast on Missing Secrets
Services SHALL validate required secrets at startup (Auth0 domain and audience, blob credentials, RabbitMQ credentials, Qdrant API key outside Development, internal gRPC key) and SHALL refuse to start when one is missing. No production secret SHALL have a default value in source code, `docker-compose.yml` or `.env.example`.

#### Scenario: Missing blob credentials in production
- **GIVEN** `ASPNETCORE_ENVIRONMENT=Production` and an empty `Blob:SecretAccessKey`
- **WHEN** the ApiService starts
- **THEN** startup SHALL fail with a clear validation error naming the missing key

#### Scenario: Development defaults
- **WHEN** the AppHost runs locally
- **THEN** generated or parameterized Aspire secrets SHALL be used instead of hardcoded literals
- **AND** the local MinIO bucket SHALL NOT be configured with anonymous public access

