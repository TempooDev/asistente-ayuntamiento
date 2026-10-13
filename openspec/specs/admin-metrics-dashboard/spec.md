# Specification: Admin Metrics Dashboard

## Purpose
Give administrators one Angular page, `admin/analytics` ("Métricas"), with the AI usage of their organization and the results of the Question Arena. Each section is shown only to the role that may read its data, and the API authorizes every call itself.

## Requirements

### Requirement: Page Access by Admin Role
The route `admin/analytics` SHALL be guarded by `authGuard` and `roleGuard` and SHALL only open for users with `admin-plataforma` or `admin-organizacion`. The "Métricas" link SHALL only be shown to those users. The guard only improves the UX; the API SHALL authorize each data call with its own policy.

#### Scenario: Administrator opens the page
- **WHEN** a signed-in user with `admin-plataforma` or `admin-organizacion` navigates to `/admin/analytics`
- **THEN** the page SHALL open with the sections allowed for their roles

#### Scenario: Regular user
- **WHEN** a signed-in user without an admin role navigates to `/admin/analytics`
- **THEN** the guard SHALL redirect them to `/chat`

#### Scenario: Anonymous user
- **WHEN** a user who is not signed in navigates to `/admin/analytics`
- **THEN** they SHALL be sent to the login page

### Requirement: Organization AI Usage
For organization administrators the page SHALL show the AI usage summary of `GET /api/ai/metrics/summary` (policy `OrganizationAdmin`): total calls with success rate, input, output and total tokens, and a breakdown per model (calls, total tokens, average latency). The summary SHALL be aggregated in the database from the tenant's `AiCallLogs` (tenant query filter) plus the ingestion totals of `IngestionMetrics`, shown as the "Ingestion Worker" entry.

#### Scenario: Organization administrator
- **WHEN** a user with `admin-organizacion` opens the page
- **THEN** the "Consumo de IA / Tokens" section SHALL show the summary of their own tenant's chat calls and the ingestion totals

#### Scenario: Platform administrator without the organization role
- **WHEN** a user with only `admin-plataforma` opens the page
- **THEN** the usage section SHALL NOT be requested or shown

#### Scenario: Summary request fails
- **WHEN** the summary request fails
- **THEN** the page SHALL show an error toast and leave the section empty instead of breaking

### Requirement: Arena Results
For platform administrators the page SHALL show the Arena results of `GET /api/arena/analytics` (policy `PlatformAdmin`), optionally filtered by `startDate` and `endDate`: total, completed and pending battles, and per pipeline (`Baseline`, `Hierarchical`) the wins, losses, ties, win rate, average latency and average estimated tokens. The table SHALL be filterable by pipeline on the client. The aggregates SHALL cover battles of every tenant and SHALL never include battle content.

#### Scenario: Filtering by date
- **WHEN** a platform administrator sets a start or end date
- **THEN** the page SHALL request the analytics again with those dates and show the new aggregates

#### Scenario: Win rate
- **WHEN** a pipeline took part in voted battles
- **THEN** its win rate SHALL be its wins divided by its battles with a vote (`Alfa`, `Beta`, `Tie` or `BothBad`), and pending battles SHALL NOT count

#### Scenario: Organization administrator without the platform role
- **WHEN** a user with only `admin-organizacion` opens the page
- **THEN** the Arena section and the date filters SHALL NOT be shown, and the API SHALL answer 403 if called directly
