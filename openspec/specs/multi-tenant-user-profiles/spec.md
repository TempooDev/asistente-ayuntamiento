# Specification: Multi-Tenant Architecture & User Profiles

## Purpose
Isolate the data of each municipality (Ayuntamiento) by Auth0 Organization with Entity Framework Core global query filters, bind every HTTP request, hub call and background work item to its tenant, and let users manage their profile and per-organization preferences.

## Requirements
### Requirement: Tenant Isolation via Auth0 Organizations
The system SHALL isolate tenant-bound data by the Auth0 Organization (`org_id` claim). `CurrentTenantService` SHALL resolve `TenantId` from, in order: the value set with `ICurrentTenantService.SetTenant` (an `AsyncLocal` override), the `org_id` claim of the current HTTP user, and, only in Development, `Tenancy:DevelopmentFallbackTenant` (default `default`). `TenantRequiredMiddleware` SHALL reject authenticated requests without a tenant.

#### Scenario: User authenticates within an Organization
- **WHEN** a request carries a token with `org_id=org_A`
- **THEN** `ICurrentTenantService.TenantId` SHALL be `org_A`

#### Scenario: Authenticated user without organization outside Development
- **GIVEN** the environment is not Development
- **WHEN** an authenticated request without an `org_id` claim reaches any endpoint or hub negotiation
- **THEN** the API SHALL respond `403 Forbidden` with a `ProblemDetails` titled `Organization required`
- **AND** SHALL NOT fall back to a shared tenant

#### Scenario: Development fallback tenant
- **GIVEN** the environment is Development
- **WHEN** a request has no `org_id`
- **THEN** the tenant SHALL be `Tenancy:DevelopmentFallbackTenant`

#### Scenario: Global data filtering
- **WHEN** the API queries `UserProfile`, `UserPreference`, `ChatSession`, `ChatMessage` (through its session), `AiCallLog`, `AiConfiguration` or `ArenaBattle`
- **THEN** EF Core SHALL apply a global query filter `TenantId == CurrentTenantId` without the query naming the tenant

### Requirement: Tenant Bound for Every Hub Call
`TenantHubFilter` SHALL bind the tenant from the `org_id` claim of the connection user (`HubCallerContext.User`) before every hub method invocation, `OnConnectedAsync` and `OnDisconnectedAsync`, and again before every item of a streaming method, so hub calls work with WebSockets, Server-Sent Events and long polling. When the connection user has no `org_id`, the filter SHALL NOT set a tenant: the Development fallback applies, and otherwise tenant-scoped hub methods SHALL fail closed with `Error [UNAUTHORIZED]`.

#### Scenario: Streaming over long polling
- **GIVEN** a client of tenant `org_A` connected with the long-polling transport
- **WHEN** it calls `StreamMessage`
- **THEN** every item of the stream SHALL be produced with `TenantId == org_A`

### Requirement: User Profile Management
The system SHALL let authenticated users view and update their profile through `/api/users/me`. Profiles SHALL be unique per `Auth0UserId` (the `sub` claim) and keep the `TenantId` of the organization where they were created.

#### Scenario: First-time profile access
- **WHEN** a user calls `GET /api/users/me` for the first time
- **THEN** the system SHALL create a `UserProfile` with their `Auth0UserId`, the current `TenantId` and the display name from the `name` claim (or the namespaced name claim)
- **AND** concurrent first calls SHALL return the same profile

#### Scenario: Updating profile details
- **WHEN** a user calls `PUT /api/users/me`
- **THEN** the API SHALL update `FullName`, `Department`, `Position` and `PhoneNumber`
- **AND** `Auth0UserId` and `TenantId` SHALL NOT change

### Requirement: Preferences per User and Organization
User preferences SHALL be stored per (`Auth0UserId`, `TenantId`), with a unique index on that pair, and read and written through `GET` and `PUT /api/users/me/preferences` for the current tenant. History analysis SHALL be started explicitly with `POST /api/users/me/preferences/analyze`, which enqueues a tenant-scoped background item and answers `202 Accepted` (or `503` when the queue is full).

#### Scenario: Same person in two organizations
- **GIVEN** user `u1` saved preferences in `org_A`
- **WHEN** `u1` reads their preferences while signed in to `org_B`
- **THEN** the API SHALL NOT return the preferences saved in `org_A`

### Requirement: Schema Segregation
The system SHALL separate bounded contexts in PostgreSQL schemas: `identity` (default schema; `UserProfiles`, `UserPreferences`, `AiConfigurations`), `chat`, `ingestion` (including `DocumentJobStates`), `scraper` and `arena`. No application table SHALL live in `public`.

#### Scenario: Applying the identity schema
- **WHEN** the EF Core migrations run
- **THEN** the `UserProfiles` table SHALL be created in the `identity` schema

### Requirement: Explicit Tenant Propagation to Background Work
Work executed outside the request (the background work queue) SHALL carry its tenant explicitly in `BackgroundWorkItem`. `BackgroundWorkItem.ForTenant` SHALL reject an empty tenant, platform-wide work SHALL be declared with `BackgroundWorkItem.PlatformWide`, and the processor SHALL call `ICurrentTenantService.SetTenant` before running a tenant-scoped item.

#### Scenario: History analysis in the background
- **GIVEN** a user of tenant `org_A` calls `POST /api/users/me/preferences/analyze`
- **WHEN** the analysis runs after the request has ended
- **THEN** it SHALL read only chat sessions of `org_A`

#### Scenario: Background item without tenant
- **WHEN** `BackgroundWorkItem.ForTenant` is called with an empty tenant
- **THEN** it SHALL throw an `ArgumentException`

#### Scenario: Platform-wide work
- **WHEN** a platform administrator triggers a manual scrape
- **THEN** the work SHALL be enqueued with `BackgroundWorkItem.PlatformWide`

### Requirement: Reviewed Use of IgnoreQueryFilters
`IgnoreQueryFilters()` SHALL only appear in files on the allow-list of `tests/AsistenteAyuntamiento.ArchitectureTests/TenantIsolationTests.cs`, which scans Application, ApiService, Infrastructure and Worker. The test checks the file list only; the predicate of each allow-listed query is reviewed by hand. The allow-list SHALL contain only:
- `AiConfigurationService` (explicit `TenantId` predicate, so background work can pass the tenant);
- `UserProfileService` (lookup by the authenticated `Auth0UserId`, unique across tenants);
- `ArenaAnalyticsService` (cross-tenant aggregates for platform administrators);
- `WinningPipelineSelector` (platform-wide win counts).

#### Scenario: Tenant-owned data lookup
- **WHEN** `AiConfigurationService` loads the configuration with `IgnoreQueryFilters()`
- **THEN** the query SHALL filter by `TenantId == CurrentTenantId`

#### Scenario: Identity-scoped profile lookup
- **WHEN** `GET /api/users/me` loads the profile of subject `u1`
- **THEN** the lookup SHALL filter by `Auth0UserId == u1` and no other subject's profile SHALL be reachable through it

#### Scenario: New unreviewed IgnoreQueryFilters usage
- **WHEN** a developer adds `.IgnoreQueryFilters(` in a file outside the allow-list
- **THEN** `IgnoreQueryFilters_is_only_used_in_allow_listed_files` SHALL fail
