# Specification: Multi-Tenant Architecture & User Profiles

## Purpose
Integrate a B2B SaaS Multi-Tenant architecture using Auth0 Organizations and Entity Framework Core. Securely isolate all data per municipality (Ayuntamiento) using Global Query Filters and allow users to manage their profile details.

## Requirements

### Requirement: Tenant Isolation via Auth0 Organizations
The system SHALL strictly isolate all data based on the Auth0 Organization (`org_id`) to ensure municipalities cannot access each other's data.

#### Scenario: User authenticates within an Organization
- **WHEN** a user logs in through an Auth0 Organization
- **THEN** the system SHALL extract the `org_id` claim from the JWT token
- **AND** the `CurrentTenantService` SHALL expose this `org_id` as the current `TenantId`
- **AND** if no `org_id` is present, it SHALL fallback safely (e.g., to "default") for local development or system administrators.

#### Scenario: Global Data Filtering
- **WHEN** the API queries any tenant-bound entity (e.g., `UserProfile`, `ChatSession`)
- **THEN** Entity Framework Core SHALL automatically apply a Global Query Filter to restrict results where `TenantId == CurrentTenantId`
- **AND** the developer SHALL NOT need to manually specify the tenant filter in standard queries.

### Requirement: User Profile Management
The system SHALL allow authenticated users to view and update their institutional profile details.

#### Scenario: First-time profile access
- **WHEN** a user accesses their profile (`GET /api/users/me`) for the first time
- **THEN** the system SHALL automatically provision a new `UserProfile` record bound to their `Auth0UserId` (from the `sub` claim) and `TenantId`
- **AND** return the empty or default profile to the client.

#### Scenario: Updating profile details
- **WHEN** a user submits changes to their profile via the Blazor web app (`PUT /api/users/me`)
- **THEN** the API SHALL update the `FullName`, `Department`, `Position`, and `PhoneNumber`
- **AND** the `Auth0UserId` and `TenantId` SHALL remain immutable
- **AND** the database schema `identity` SHALL store these updates securely.

### Requirement: Schema Segregation
The system SHALL use database schemas to cleanly separate bounded contexts within PostgreSQL.

#### Scenario: Applying the Identity Schema
- **WHEN** the EF Core migrations execute
- **THEN** the system SHALL place the `UserProfile` table and related identity data into the `identity` PostgreSQL schema
- **AND** ensure it does not pollute the default `public` schema or future schemas like `vector`.
