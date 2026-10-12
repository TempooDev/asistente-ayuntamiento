## MODIFIED Requirements

### Requirement: Tenant Isolation via Auth0 Organizations
The system SHALL strictly isolate all data based on the Auth0 Organization (`org_id`) to ensure municipalities cannot access each other's data. Outside the Development environment, an authenticated request without `org_id` SHALL be rejected.

#### Scenario: User authenticates within an Organization
- **WHEN** a user logs in through an Auth0 Organization
- **THEN** the system SHALL extract the `org_id` claim from the JWT token
- **AND** the `CurrentTenantService` SHALL expose this `org_id` as the current `TenantId`

#### Scenario: Authenticated user without organization in production
- **GIVEN** the environment is not Development
- **WHEN** an authenticated request without an `org_id` claim reaches any tenant-scoped endpoint or hub
- **THEN** the API SHALL respond `403 Forbidden`
- **AND** SHALL NOT fall back to a shared tenant

#### Scenario: Development fallback tenant
- **GIVEN** the environment is Development
- **WHEN** a request has no `org_id`
- **THEN** the system SHALL use the configured `Tenancy:DevelopmentFallbackTenant` value

#### Scenario: Global Data Filtering
- **WHEN** the API queries any tenant-bound entity (e.g., `UserProfile`, `ChatSession`, `AiConfiguration`, `AiCallLog`, `ArenaBattle`)
- **THEN** Entity Framework Core SHALL automatically apply a Global Query Filter to restrict results where `TenantId == CurrentTenantId`
- **AND** the developer SHALL NOT need to manually specify the tenant filter in standard queries.

## ADDED Requirements

### Requirement: Explicit Tenant Propagation to Background Work
Any work executed outside the HTTP request scope (background queues, hosted services, fire-and-forget analysis, message consumers) SHALL receive the tenant identifier explicitly and SHALL execute inside `ICurrentTenantService.BeginScope(tenantId)`. Work SHALL NOT resolve the tenant from `IHttpContextAccessor`.

#### Scenario: History analysis after preferences update
- **GIVEN** a user of tenant `org_A` triggers a history analysis from `PUT /api/users/me/preferences`
- **WHEN** the analysis runs in the background
- **THEN** it SHALL query chat sessions filtered by `org_A`
- **AND** SHALL produce the same results as if it ran inside the request

#### Scenario: Background item without tenant
- **WHEN** a background work item is enqueued without a tenant identifier
- **THEN** the queue SHALL reject it with an `ArgumentException`
- **AND** SHALL log the rejection

### Requirement: Explicit Tenant Predicate When Ignoring Query Filters
Any query that calls `IgnoreQueryFilters()` SHALL add an explicit `TenantId` predicate, unless it appears in an allow-list of documented cross-tenant administrative queries. An architecture test SHALL enforce this rule.

#### Scenario: User preferences lookup
- **WHEN** `UserPreferenceService` loads preferences for `Auth0UserId = u1` in tenant `org_A`
- **THEN** the query SHALL filter by both `Auth0UserId == u1` and `TenantId == org_A`
- **AND** a preference row of `u1` stored under `org_B` SHALL NOT be returned

#### Scenario: New unreviewed IgnoreQueryFilters usage
- **WHEN** a developer adds `IgnoreQueryFilters()` in a file outside the allow-list
- **THEN** the architecture test suite SHALL fail
