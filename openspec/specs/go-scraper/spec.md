# Specification: Go Scraper (BOE, BOJA)

## Purpose
A Go service (`src/go-scraper`, binary `cmd/scraper`) downloads the documents of the official bulletins, keeps those that pass the filter rules, stores them in the shared S3-compatible bucket and queues them for the .NET ingestion workers. The project covers two bulletins: BOE and BOJA.

Configuration and the package layout are documented in `docs/go-scraper-integration.md`; the queue contract in `docs/messaging-contract.md`.

## Sources

### BOE — open data API
- Summary: `GET https://www.boe.es/datosabiertos/api/boe/sumario/{YYYYMMDD}` (JSON, `Accept: application/json`).
- Document: `GET https://www.boe.es/diario_boe/xml.php?id={id}` (XML with metadata and text).
- Only `BOE-A-*` documents under the selected sections are kept (default `1`, `2B`, `3`, `5A`).

### BOJA — Atom feeds and bulletin index pages
- Today: the section feeds `/boja/distribucion/s51.xml`–`s55.xml` list the latest bulletin (they carry no date).
- Past days: the numbered index pages `/boja/{year}/{number}/index.html` are binary-searched by the date in their heading (`BOJA nº 130 de 08/07/2026`).
- Document ids derive from the URL: `/boja/2026/123/1` → `BOJA-2026-123-1`; the publication date is the bulletin's.

## Requirements

### Requirement: Polite, Resilient HTTP Access
All requests to the bulletin sites SHALL go through the shared client (`internal/httpx`): per-site rate limit (BOE 2 req/s, BOJA 1 req/s), per-attempt timeout, retries with exponential backoff and jitter on network errors, 429 and 5xx, and no retries on other 4xx.

#### Scenario: Temporary server error
- **WHEN** boe.es answers 503 and then 200
- **THEN** the request SHALL succeed after the retry

#### Scenario: Day without bulletin
- **WHEN** the BOE summary answers 404 (Sundays, holidays) or no BOJA bulletin has that date
- **THEN** the day SHALL produce no documents and no error

#### Scenario: Shutdown during a retry wait
- **WHEN** the process is stopping while a request waits to retry
- **THEN** the wait SHALL end immediately with the cancellation

### Requirement: Filter Rules, Fail Closed
Every job SHALL load the active filter rules from the .NET API (`FilterConfigService.GetActiveFilters` over gRPC, with `x-internal-key`). `Keyword` and `Department` rules filter documents; `Section` (BOE) and `BojaFeed` (BOJA) rules select what is fetched. Sections passed in a manual request take precedence over `Section` rules.

#### Scenario: Rules unavailable
- **WHEN** the rules cannot be loaded (API down, invalid key)
- **THEN** the job SHALL fail without downloading anything, instead of downloading every document

#### Scenario: Document without a matching rule
- **WHEN** a source has content rules and a document matches none of them
- **THEN** the document SHALL be discarded and counted as filtered

### Requirement: Storage Before Queueing
For each kept document the scraper SHALL write the original to `raw-{ext}/{SOURCE}/{id}.{ext}` with its real MIME type (BOE XML, BOJA HTML), then the parsed document to `json/{SOURCE}/{id}.json`, and only then publish the ingestion message.

#### Scenario: Bucket write fails
- **WHEN** the JSON document cannot be stored
- **THEN** no message SHALL be published and the document SHALL be counted as an error at the `store` stage

#### Scenario: Backup of the original fails
- **WHEN** only the raw original cannot be stored
- **THEN** the document SHALL still be stored and queued, and a warning logged

### Requirement: Durable Ingestion Messages
The scraper SHALL publish `{ "source", "document_id", "blob_path" }` as a persistent message with publisher confirms to both `documents_to_process_baseline` and `documents_to_process_hierarchical`, declaring the queues exactly as the .NET topology (retry and dead-letter queues). Without `ConnectionStrings__messaging`, documents are stored but not queued.

#### Scenario: Broker restarts
- **WHEN** RabbitMQ is restarted after a message was confirmed
- **THEN** the message SHALL still be in the queue

#### Scenario: Connection lost between documents
- **WHEN** the AMQP connection was closed
- **THEN** the publisher SHALL reconnect once and retry before reporting the error

### Requirement: One Job at a Time
Scrape jobs SHALL come from the daily schedule (previous day at `SCRAPE_SCHEDULE_HOUR`, providers `SCRAPE_SCHEDULED_PROVIDERS`), the HTTP API (`/api/scrape`, asynchronous, status at `/api/jobs/{id}`) or the gRPC command `ForceScrape` (synchronous, used by the .NET API). All three SHALL share one lock. Ranges SHALL be validated: start required, end not before start nor in the future, at most `SCRAPE_MAX_RANGE_DAYS` days.

#### Scenario: Job requested while another runs
- **WHEN** a job is requested while one is running
- **THEN** the HTTP API SHALL answer 409, the gRPC command SHALL answer `Success = false`, and the schedule SHALL skip its run

#### Scenario: Invalid range
- **WHEN** a manual job asks for 60 days
- **THEN** it SHALL be rejected (HTTP 400 / `Success = false`) before any download

### Requirement: Authenticated Command Service
The gRPC `ScraperCommandService` SHALL require the shared `x-internal-key` when `INTERNAL_GRPC_KEY` is set; the .NET API sends it on every call.

#### Scenario: Call without the key
- **WHEN** a caller invokes `ForceScrape` without the key or with a wrong one
- **THEN** the call SHALL fail with `Unauthenticated`

### Requirement: Graceful Shutdown and Observability
On SIGINT/SIGTERM the scraper SHALL stop accepting requests, let in-flight gRPC calls finish, cancel the running job between documents and flush telemetry, within 30 seconds. Logs SHALL be JSON (`slog`) with `trace_id`/`span_id`; spans SHALL be nested per job and document; counters SHALL report saved, filtered and failed documents (by stage) and failed summaries.

#### Scenario: Deploy during a job
- **WHEN** the container is stopped while a job runs
- **THEN** the job SHALL end as failed (cancelled) and the process SHALL exit cleanly

#### Scenario: Health check
- **WHEN** Aspire or the load balancer calls `GET /health`
- **THEN** the scraper SHALL answer 200
