# Specification: Dynamic Scraper Filtering and gRPC Administration

## Purpose
Let platform administrators control from the Angular UI which BOE and BOJA documents the Go scraper fetches and keeps, through filter rules stored in PostgreSQL and served to the scraper over gRPC, and let them trigger a scrape on demand through a gRPC command hosted by the scraper.

## Requirements

### Requirement: Database-Driven Filtering Configuration
The .NET backend SHALL persist scraper filter rules as `ScraperFilterRule` entities (`scraper."ScraperFilterRules"`, not tenant-filtered) with `Id`, `Provider` (`BOE` or `BOJA`), `FilterType` (`Department`, `Section`, `Keyword` or `BojaFeed`, stored as text), `Value`, `IsActive` and `CreatedAt`.

#### Scenario: API management of filters
- **WHEN** a platform administrator manages the rules from the Angular settings page
- **THEN** the SPA SHALL use `GET`, `POST /api/scraper/filters`, `GET`, `PUT`, `DELETE /api/scraper/filters/{id}`, all under `Policies.PlatformAdmin`
- **AND** an unknown id SHALL answer `404 Not Found`

#### Scenario: Listing fails
- **WHEN** reading the rules fails
- **THEN** `GET /api/scraper/filters` SHALL answer with an error, never with an empty list

### Requirement: Centralized Scraper Filter Management Service
The system SHALL provide `IScraperFilterService` (`AsistenteAyuntamiento.Application.Features.Scraper`) for every query and change of filter rules.

#### Scenario: Service-driven filter queries
- **WHEN** the gRPC service or the HTTP endpoints read or change filter rules
- **THEN** they SHALL call `IScraperFilterService` instead of running Entity Framework Core queries themselves

### Requirement: gRPC Microservices Communication
The Go scraper and the .NET API SHALL communicate through the contract in `src/Protos/scraper.proto`, which defines `FilterConfigService.GetActiveFilters` (hosted by .NET) and `ScraperCommandService.ForceScrape` (hosted by Go). Both directions SHALL authenticate with the shared `x-internal-key` metadata (`Internal:GrpcApiKey` in .NET, `INTERNAL_GRPC_KEY` in Go).

#### Scenario: Filter service called without the key
- **WHEN** a caller invokes `GetActiveFilters` without a valid `x-internal-key` and `Internal:GrpcApiKey` is configured
- **THEN** the API SHALL fail the call with `Unauthenticated`
- **AND** when the key is not configured the API SHALL accept the call only in Development

### Requirement: Go Scraper Dynamic Configuration (gRPC Client)
The Go scraper SHALL fetch the active rules from `GetActiveFilters()` once per job, with a 30-second timeout and the `x-internal-key` metadata, and SHALL fail closed when they cannot be obtained.

#### Scenario: Retrieving rules before scraping
- **WHEN** a scheduled, HTTP or gRPC job starts
- **THEN** the scraper SHALL load the active rules once
- **AND** documents rejected by the content rules SHALL be discarded in memory and NOT uploaded to the bucket

#### Scenario: Filter service unavailable or erroring
- **WHEN** reading the rules fails in the .NET service
- **THEN** the service SHALL return the gRPC status `Internal`, not an empty rule list
- **AND** the scraper SHALL end the job as failed, with the error logged, without downloading any document

#### Scenario: TLS to the filter service
- **WHEN** `DOTNET_API_GRPC_URL` starts with `https://`
- **THEN** the scraper SHALL use TLS 1.2 or later and verify the server certificate against the system trust store
- **AND** SHALL NOT disable certificate verification

### Requirement: On-Demand Scrape
Platform administrators SHALL be able to trigger a scrape from the UI with `POST /api/scraper/filters/trigger` (`{ provider, startDate?, endDate?, sections? }`). The API SHALL allow one manual scrape per instance at a time (`ScraperStateService`), run the gRPC `ForceScrape` call as tracked background work and report the outcome over SignalR. `ForceScrape` SHALL run the job synchronously in the scraper and report business failures as `Success = false` with a message, not as gRPC error statuses. The API's `ForceScrape` client SHALL allow up to 30 minutes per call and SHALL NOT retry it (a retry would start a second scrape); it SHALL NOT inherit the default HTTP resilience handler (10-second attempts with retries).

#### Scenario: Triggering a scrape
- **WHEN** a platform administrator triggers a BOE scrape from 2026-10-01 to 2026-10-05
- **THEN** the API SHALL answer `202 Accepted` and send `ScraperStateChanged { isScraping: true }`
- **AND** call `ForceScrape`, which SHALL return when the job ends with `Success`, a message and `ItemsExtracted` (documents saved)
- **AND** the API SHALL then send `ScraperStateChanged { isScraping: false }` with an empty message on success, or with the scraper's message when `Success = false`

#### Scenario: Dates omitted
- **WHEN** `ForceScrape` receives an empty start date
- **THEN** it SHALL scrape today (an empty end date means the start date)

#### Scenario: Invalid date range
- **WHEN** `ForceScrape` receives an unparsable date, an end before the start, an end in the future or a range longer than `SCRAPE_MAX_RANGE_DAYS`
- **THEN** it SHALL return `Success = false` with the reason
- **AND** SHALL NOT start any job

#### Scenario: Concurrent trigger
- **WHEN** a manual scrape is already running in this API instance
- **THEN** `POST /api/scraper/filters/trigger` SHALL answer `409 Conflict` without calling the scraper
- **AND** when another job runs in the scraper (schedule, HTTP or gRPC), `ForceScrape` SHALL return `Success = false` and the HTTP API `409`

#### Scenario: Background queue full
- **WHEN** the background work queue rejects the trigger
- **THEN** the API SHALL answer `503 Service Unavailable` and clear the running flag

#### Scenario: Unauthorized trigger
- **WHEN** a user without `admin-plataforma` calls `POST /api/scraper/filters/trigger`
- **THEN** the API SHALL respond `403 Forbidden` and SHALL NOT call the scraper

#### Scenario: Long manual scrape
- **WHEN** a manual scrape takes several minutes, or the scraper answers the call with a transport error
- **THEN** the API SHALL wait for the result (up to 30 minutes) and SHALL send `ForceScrape` only once

### Requirement: Filter Semantics
Rules SHALL apply to the provider they name, compared case-insensitively (a rule without provider applies to both). A `Keyword` rule SHALL match when its normalized value (lower case, Spanish accents folded) is a substring of the document's normalized title or full text. A `Department` rule SHALL match when its normalized value equals the document's normalized department. A document SHALL pass when its provider has no content rules or when at least one of them matches. `BojaFeed` and `Section` rules (documents carry no feed or section metadata) and rules with an empty value SHALL NOT count as content rules.

#### Scenario: Keyword matches
- **GIVEN** an active BOE rule `Keyword = "subvención"`
- **WHEN** a BOE document titled "Bases de SUBVENCIONES para ayuntamientos" is evaluated
- **THEN** the document SHALL pass the filter

#### Scenario: Keyword in the text only
- **GIVEN** an active BOE rule `Keyword = "subvención"`
- **WHEN** a document titled "Orden de 3 de octubre" whose text mentions "subvenciones" is evaluated
- **THEN** the document SHALL pass the filter

#### Scenario: Keyword does not match
- **GIVEN** an active BOE rule `Keyword = "subvención"`
- **WHEN** a BOE document titled "Nombramiento de personal" whose text does not mention it is evaluated
- **THEN** the document SHALL be discarded

#### Scenario: Only configuration rules exist
- **GIVEN** the only active rules are of type `BojaFeed`
- **WHEN** BOE documents are evaluated
- **THEN** they SHALL pass, because no content rule applies to BOE

#### Scenario: BOJA feeds from rules
- **GIVEN** active BOJA rules of type `BojaFeed`
- **WHEN** a BOJA job reads today's bulletin
- **THEN** it SHALL read those feed URLs instead of the default ones

### Requirement: Per-Job Scrape Options
Scrape options (sections, feeds, date range) SHALL be passed as arguments of each job and SHALL NOT be stored by mutating long-lived provider instances.

#### Scenario: Forced scrape with sections followed by the nightly job
- **GIVEN** an administrator forced a BOE scrape limited to section `2B`
- **WHEN** the scheduled job runs afterwards
- **THEN** it SHALL use the `Section` rules or, without them, the default BOE sections, not `2B`
