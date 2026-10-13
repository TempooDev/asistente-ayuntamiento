# Specification: Go Scraper (BOE, BOJA)

## Purpose
A Go service (`src/go-scraper`, binary `cmd/scraper`) downloads the documents of the official bulletins in the project's scope, BOE and BOJA, keeps those that pass the filter rules, stores them in the shared S3-compatible bucket and queues them for the .NET ingestion workers. Jobs come from a daily schedule, an HTTP API and a gRPC command used by the .NET API. Configuration and the package layout are documented in `docs/go-scraper-integration.md`; the queue contract in `docs/messaging-contract.md`.

## Sources

### BOE — open data API
- Summary: `GET https://www.boe.es/datosabiertos/api/boe/sumario/{YYYYMMDD}` (JSON, `Accept: application/json`).
- Document: `GET https://www.boe.es/diario_boe/xml.php?id={id}` (XML with metadata and text).
- Only `BOE-A-*` documents under the selected sections are kept (default `1`, `2B`, `3`, `5A`).

### BOJA — Atom feeds and bulletin index pages
- Today: the section feeds `/boja/distribucion/s51.xml`–`s55.xml` (or `BOJA_FEEDS`, or `BojaFeed` rules) list the latest bulletin (they carry no date).
- Past days: the numbered index pages `/boja/{year}/{number}/index.html` are binary-searched by the date in their heading (`BOJA nº 130 de 08/07/2026`).
- Document ids derive from the URL: `/boja/2026/123/1` → `BOJA-2026-123-1`; the publication date is the bulletin's (`yyyy-MM-dd`).

## Requirements

### Requirement: Polite, Resilient HTTP Access
All requests to the bulletin sites SHALL go through the shared client (`internal/httpx`): per-site rate limit (BOE 2 req/s, BOJA 1 req/s), a 30-second timeout per attempt, up to 3 attempts with exponential backoff and jitter on network errors, 429 and 5xx, no retries on other 4xx, and responses limited to 50 MiB.

#### Scenario: Temporary server error
- **WHEN** boe.es answers 503 and then 200
- **THEN** the request SHALL succeed after the retry

#### Scenario: Day without bulletin
- **WHEN** the BOE summary answers 404 (Sundays, holidays) or no BOJA bulletin has that date
- **THEN** the day SHALL produce no documents and no error

#### Scenario: Summary that cannot be read
- **WHEN** a day's summary fails for another reason
- **THEN** the failure SHALL be logged and counted in `scraper.summaries.errors`, and the job SHALL continue with the next day

#### Scenario: Shutdown during a retry wait
- **WHEN** the job is cancelled while a request waits to retry
- **THEN** the wait SHALL end immediately with the cancellation

### Requirement: Filter Rules, Fail Closed
Every job SHALL load the active filter rules once from the .NET API (`FilterConfigService.GetActiveFilters` over gRPC at `DOTNET_API_GRPC_URL`, 30-second timeout, sending `x-internal-key`). `Keyword` and `Department` rules filter documents of their provider; `Section` (BOE) and `BojaFeed` (BOJA) rules select what is fetched. Sections passed in a manual request take precedence over `Section` rules.

#### Scenario: Rules unavailable
- **WHEN** the rules cannot be loaded (API down, invalid key, server error)
- **THEN** the job SHALL fail without downloading any document, instead of downloading every document

#### Scenario: Document without a matching rule
- **WHEN** a source has content rules and a document matches none of them
- **THEN** the document SHALL be discarded and counted in `scraper.documents.filtered`

### Requirement: Storage Before Queueing
For each kept document the scraper SHALL write the original to `raw-{ext}/{SOURCE}/{id}.{ext}` with its MIME type (BOE `raw-xml/BOE/{id}.xml`, BOJA `raw-html/BOJA/{id}.html`), then the parsed document to `json/{SOURCE}/{id}.json`, and only then publish the ingestion message. Up to 5 documents of a day are processed at once.

#### Scenario: Bucket write fails
- **WHEN** the JSON document cannot be stored
- **THEN** no message SHALL be published and the document SHALL be counted in `scraper.documents.errors` with stage `store`

#### Scenario: Backup of the original fails
- **WHEN** only the original cannot be stored
- **THEN** the document SHALL still be stored and queued, and a warning logged
- **AND** for BOE the hierarchical ingestion SHALL still ingest the document, splitting the JSON text as a single article instead of the XML articles

### Requirement: Durable Ingestion Messages
The scraper SHALL publish `{ "source", "document_id", "blob_path" }` as a persistent message with publisher confirms to both `documents_to_process_baseline` and `documents_to_process_hierarchical`, declaring the queues exactly as the .NET topology (retry and dead-letter queues). Without `ConnectionStrings__messaging`, documents are stored but not queued.

#### Scenario: Broker restarts
- **WHEN** RabbitMQ is restarted after a message was confirmed
- **THEN** the message SHALL still be in the queue

#### Scenario: Connection lost between documents
- **WHEN** the AMQP connection was closed
- **THEN** the publisher SHALL reconnect once and retry before reporting the error

#### Scenario: Publish fails
- **WHEN** a stored document cannot be published
- **THEN** it SHALL be counted with stage `publish` and stay in the bucket, where the administrator's reprocess action can queue it

### Requirement: One Job at a Time
Scrape jobs SHALL come from the daily schedule (the previous day at local hour `SCRAPE_SCHEDULE_HOUR`, default 1, for `SCRAPE_SCHEDULED_PROVIDERS`, default `BOE`), the HTTP API (`GET` or `POST /api/scrape?start=&end=&provider=&section=`, asynchronous, answered with 202 and a `Location` to poll at `GET /api/jobs/{id}`) or the gRPC command `ForceScrape` (synchronous, used by the .NET API). All three SHALL share one lock in the orchestrator. Ranges SHALL be validated: start required (gRPC: an empty start means today), an empty end means the start day, end neither before start nor in the future, and at most `SCRAPE_MAX_RANGE_DAYS` days (default 31).

#### Scenario: Job requested while another runs
- **WHEN** a job is requested while one is running
- **THEN** the HTTP API SHALL answer 409, the gRPC command SHALL answer `Success = false`, and the schedule SHALL skip its run

#### Scenario: Invalid range
- **WHEN** a manual job asks for 60 days
- **THEN** it SHALL be rejected (HTTP 400 / `Success = false` with the reason) before any download

#### Scenario: Job status
- **WHEN** a client polls `GET /api/jobs/{id}` for one of the last 50 jobs
- **THEN** it SHALL receive the job's trigger, providers, range, status (`running`, `succeeded`, `failed`), saved documents and error

### Requirement: Authenticated Command Service
`INTERNAL_GRPC_KEY` SHALL be required (at least 16 characters; the AppHost generates 32) and the scraper SHALL refuse to start without it. The gRPC `ScraperCommandService` SHALL require it in the `x-internal-key` metadata (constant-time comparison); the .NET API sends it on every call.

#### Scenario: Missing key
- **WHEN** the scraper starts without `INTERNAL_GRPC_KEY`
- **THEN** configuration loading SHALL fail and the process SHALL exit

#### Scenario: Call without the key
- **WHEN** a caller invokes `ForceScrape` without the key or with a wrong one
- **THEN** the call SHALL fail with `Unauthenticated`

### Requirement: Graceful Shutdown and Observability
On SIGINT/SIGTERM the scraper SHALL stop the schedule, shut down the HTTP server, cancel every running job whatever started it (`Orchestrator.Stop()`; a job stops between documents and ends as failed), stop the gRPC server letting in-flight calls return, and wait for the jobs, all within 25 seconds, then flush telemetry within 5 more seconds. After `Stop()` a new job SHALL be rejected with `ErrStopping` (HTTP 503, gRPC `Success = false`). Logs SHALL be JSON (`slog`) with `trace_id`/`span_id`; spans SHALL be nested per job and document; counters SHALL report saved, filtered and failed documents (by stage `fetch`, `store`, `publish`) and failed summaries.

#### Scenario: Deploy during a gRPC job
- **WHEN** the container is stopped while a `ForceScrape` job runs
- **THEN** the job SHALL end as failed (cancelled), `ForceScrape` SHALL return `Success = false`, and the process SHALL exit within 30 seconds

#### Scenario: Job requested during shutdown
- **WHEN** a job is requested after `Stop()` (for example a `ForceScrape` call that arrives before the gRPC server stops)
- **THEN** it SHALL be rejected with `ErrStopping` and no download SHALL start

#### Scenario: Health check
- **WHEN** Aspire or the load balancer calls `GET /health`
- **THEN** the scraper SHALL answer 200
