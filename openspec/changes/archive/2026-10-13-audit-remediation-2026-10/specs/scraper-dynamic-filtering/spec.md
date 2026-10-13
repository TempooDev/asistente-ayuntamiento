## MODIFIED Requirements

### Requirement: Go Scraper Dynamic Configuration (gRPC Client)
The Go scraper SHALL retrieve its filtering configuration from the .NET backend using gRPC over a verified channel, authenticated with the internal key, and SHALL fail closed when the configuration cannot be obtained.

#### Scenario: Retrieving rules before scraping
- **WHEN** the Go scraper initiates a scheduled or manual scrape job
- **THEN** it SHALL call `GetActiveFilters()` once per job, with a timeout and the `x-internal-key` metadata
- **AND** it SHALL apply the returned rules to evaluate metadata provided by the Open Data APIs (BOE/BOJA)
- **AND** documents failing the filter SHALL be discarded in-memory and NOT uploaded to S3

#### Scenario: Filter service unavailable or erroring
- **WHEN** `GetActiveFilters()` fails (network error, timeout, or a server error status)
- **THEN** the .NET service SHALL return a gRPC error status, not an empty rule list
- **AND** the scraper SHALL abort the job without downloading any document
- **AND** SHALL log the failure and report the job as failed

#### Scenario: TLS to the filter service
- **WHEN** the filter service URL uses `https://`
- **THEN** the scraper SHALL verify the server certificate with the system pool or a configured CA
- **AND** SHALL NOT use `InsecureSkipVerify`

### Requirement: On-Demand Scrape (gRPC Server on Go)
The system SHALL support triggering a scrape job from the UI using gRPC. The job SHALL run asynchronously, its parameters SHALL be validated, and it SHALL be serialized with every other scrape execution path.

#### Scenario: Triggering a Scrape Command
- **GIVEN** an administrator wants to scrape a specific section of BOJA immediately
- **WHEN** they trigger the action in the Angular UI
- **THEN** the .NET API SHALL call `ForceScrape()` on the Go scraper's gRPC server
- **AND** the scraper SHALL validate the date range, start the job asynchronously, and return a job identifier

#### Scenario: Invalid date range
- **WHEN** `ForceScrape()` receives an unparsable date, `start > end`, or a range longer than the configured maximum
- **THEN** it SHALL return `InvalidArgument`
- **AND** SHALL NOT start any job

#### Scenario: Concurrent trigger
- **GIVEN** a scrape job is running (started by cron, HTTP or gRPC)
- **WHEN** another trigger arrives through any path
- **THEN** the scraper SHALL reject it with `FailedPrecondition` / HTTP 409
- **AND** SHALL NOT mutate shared provider state

#### Scenario: Unauthorized trigger
- **WHEN** a non-admin user calls `POST /api/scraper/filters/trigger`
- **THEN** the API SHALL respond `403 Forbidden` and SHALL NOT call the scraper

## ADDED Requirements

### Requirement: Keyword Filter Semantics
A `Keyword` rule SHALL match a document when its normalized value (case-insensitive, accent-insensitive) is a substring of the document's normalized title or summary. Rules of configuration types (`BojaFeed`, `Section`: documents carry no feed or section metadata) and rules with an empty value SHALL NOT count as content filters.

#### Scenario: Keyword matches
- **GIVEN** an active rule `Keyword = "subvención"`
- **WHEN** a document titled "Bases de SUBVENCIONES para ayuntamientos" is evaluated
- **THEN** the document SHALL pass the filter

#### Scenario: Keyword does not match
- **GIVEN** an active rule `Keyword = "subvención"`
- **WHEN** a document titled "Nombramiento de personal" is evaluated
- **THEN** the document SHALL be discarded

#### Scenario: Only configuration rules exist
- **GIVEN** the only active rules are of type `BojaFeed`
- **WHEN** BOE documents are evaluated
- **THEN** they SHALL pass, because no content filter applies to BOE

### Requirement: Per-Job Scrape Options
Scrape options (sections, feeds, date range) SHALL be passed as arguments of each job and SHALL NOT be stored by mutating long-lived provider instances.

#### Scenario: Forced scrape with sections followed by the nightly cron
- **GIVEN** an administrator forced a BOE scrape limited to section `2B`
- **WHEN** the nightly cron job runs afterwards
- **THEN** the cron job SHALL use the default BOE sections, not `2B`
