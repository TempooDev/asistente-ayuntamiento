# Specification: Admin Metrics Dashboard

## Purpose
Give administrators one Angular page, `admin/analytics` ("Métricas"), with the AI usage of their organization, the platform's ingestion cost and the results of the Question Arena. Each section is shown only to the role that may read its data, and the API authorizes every call itself.

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
For organization administrators the page SHALL show the AI usage summary of `GET /api/ai/metrics/summary` (policy `OrganizationAdmin`): total calls with their success rate, succeeded and failed calls, input, output and total tokens, and a breakdown per model (calls, total tokens, average latency). The summary SHALL be aggregated in the database from the tenant's `AiCallLogs` only (tenant query filter); it MUST NOT include platform-wide data such as `IngestionMetrics`. `successRate` SHALL be a percentage from 0 to 100 and the page SHALL show it as such (95 is "95 %"), never through a pipe that multiplies it by 100.

#### Scenario: Organization administrator
- **WHEN** a user with `admin-organizacion` opens the page
- **THEN** the "Consumo de IA / Tokens" section SHALL show the summary of their own tenant's chat calls and nothing from other tenants or from ingestion

#### Scenario: Success rate
- **GIVEN** the tenant made 40 calls and 38 succeeded
- **WHEN** the summary is shown
- **THEN** the "Llamadas" card SHALL show 40 calls, "95 % con éxito", 38 succeeded and 2 failed

#### Scenario: Platform administrator without the organization role
- **WHEN** a user with only `admin-plataforma` opens the page
- **THEN** the usage section SHALL NOT be requested or shown

#### Scenario: Summary request fails
- **WHEN** the summary request fails
- **THEN** the page SHALL show an error toast and leave the section empty instead of breaking

### Requirement: Platform Ingestion Cost
For platform administrators the page SHALL show the platform-wide ingestion cost of `GET /api/ai/metrics/ingestion` (policy `PlatformAdmin`), aggregated from `IngestionMetrics`: ingested documents, average processing time, LLM enrichment calls, enrichment tokens and embedded tokens, each with its own label. Enrichment calls SHALL NOT be labelled as embeddings.

#### Scenario: Platform administrator
- **WHEN** a user with `admin-plataforma` opens the page
- **THEN** the "Consumo de ingesta (plataforma)" section SHALL show the ingestion totals of every pipeline

#### Scenario: Organization administrator without the platform role
- **WHEN** a user with only `admin-organizacion` opens the page
- **THEN** the ingestion section SHALL NOT be requested or shown, and the API SHALL answer 403 if called directly

### Requirement: Arena Results
For platform administrators the page SHALL show the Arena results of `GET /api/arena/analytics` (policy `PlatformAdmin`), optionally filtered by `startDate` and `endDate`: total, completed and pending battles, and per pipeline (`Baseline`, `Hierarchical`) the wins, losses, ties, "both bad" votes, win rate, average latency and average estimated tokens. The table SHALL be filterable by pipeline on the client. The aggregates SHALL cover battles of every tenant and SHALL never include battle content.

#### Scenario: Filtering by date
- **WHEN** a platform administrator sets a start or end date
- **THEN** the page SHALL request the analytics again with those dates and show the new aggregates

#### Scenario: Win rate
- **WHEN** a pipeline took part in voted battles
- **THEN** its win rate SHALL be its wins divided by its decisive votes (wins plus losses); ties, "both bad" votes and pending battles SHALL NOT count in the denominator
- **AND** ties and "both bad" votes SHALL be shown in their own columns

#### Scenario: Organization administrator without the platform role
- **WHEN** a user with only `admin-organizacion` opens the page
- **THEN** the Arena section and the date filters SHALL NOT be shown, and the API SHALL answer 403 if called directly
