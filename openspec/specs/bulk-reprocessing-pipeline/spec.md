# Specification: Bulk Reprocessing Pipeline with Dual Worker Deployment

## Purpose
Let platform administrators (re)ingest the stored bulletin documents through the baseline (fixed-overlap chunks) and hierarchical (parent-child) pipelines, each run by its own Worker instance built from the same image, so that both pipelines process the same documents and record comparable `IngestionMetrics`.
## Requirements
### Requirement: Pipeline Mode Configuration
The Worker SHALL read `WORKER_PIPELINE_MODE` at startup and register exactly one ingestion consumer: `HierarchicalIngestionConsumer` for `HIERARCHICAL`, the flat-chunk consumer of the `Recursive` variant for `RECURSIVE`, the one of the `Semantic` variant for `SEMANTIC` (all case-insensitive), and `BaselineIngestionConsumer` for `BASELINE`, for an unset variable, or for any value that is not a `PipelineType`. Each pipeline SHALL have its own queue with `.retry` and `.dlq` and the retry and dead-letter policy of `RabbitMqConsumerBase`, and its own status in `DocumentJobState` (`BaselineStatus`, `HierarchicalStatus`, `RecursiveStatus`, `SemanticStatus`; `null` = never requested). The summary `Status` shown to administrators SHALL be computed from the production pipelines only (`BaselineStatus` and `HierarchicalStatus`): a study status (`RecursiveStatus`, `SemanticStatus`) is kept and reported per pipeline but SHALL NOT change the summary, so an unconsumed study queue never leaves a document `Queued` nor turns its summary into `Failed`.

#### Scenario: Starting in baseline mode
- **WHEN** the Worker starts with `WORKER_PIPELINE_MODE=BASELINE`
- **THEN** it SHALL consume only from `documents_to_process_baseline`
- **AND** write `DocumentChunks` rows with `ChunkingStrategy = FixedOverlap` in PostgreSQL and points in the Qdrant collection `document_chunks`

#### Scenario: Starting in hierarchical mode
- **WHEN** the Worker starts with `WORKER_PIPELINE_MODE=HIERARCHICAL`
- **THEN** it SHALL consume only from `documents_to_process_hierarchical`
- **AND** dispatch each message to the keyed `IHierarchicalIngestionProcessor` of its source (`BoeIngestionService`, `BojaIngestionService`), which writes `ParentDocuments` and `ChildFragments` rows and points in the Qdrant collection `child_fragments`

#### Scenario: Starting in a study mode
- **WHEN** the Worker starts with `WORKER_PIPELINE_MODE=SEMANTIC`
- **THEN** it SHALL consume only from `documents_to_process_semantic`
- **AND** write `DocumentChunks` rows with `ChunkingStrategy = Semantic` and points in `document_chunks_semantic`, and set only the document's `SemanticStatus`

#### Scenario: A study status does not change the summary
- **WHEN** a document's baseline and hierarchical statuses are `Completed` and its `SemanticStatus` is `Queued` (no study worker running) or `Failed`
- **THEN** its summary `Status` SHALL be `Completed`
- **AND** the per-pipeline counters SHALL still report the semantic document as queued or failed

#### Scenario: Missing or unknown mode
- **WHEN** the Worker starts without `WORKER_PIPELINE_MODE` or with a value that is not a known mode
- **THEN** it SHALL run in baseline mode

### Requirement: Dual Worker Deployment
The deployment SHALL run one Worker instance per pipeline from the same project/image. The Aspire AppHost SHALL run `worker-baseline`, `worker-hierarchical`, `worker-recursive` and `worker-semantic`. `docker-compose.yml` SHALL run `worker-baseline` and `worker-hierarchical` by default and `worker-recursive` and `worker-semantic` only with the Compose profile `chunking-study`.

#### Scenario: Parallel processing
- **WHEN** the workers are running and a document is queued for several pipelines
- **THEN** each worker SHALL consume its own queue without competing for the other's messages
- **AND** each SHALL record an `IngestionMetrics` row with its `PipelineType` when the document is ingested

#### Scenario: Production without the study profile
- **WHEN** `docker compose up` runs without `--profile chunking-study`
- **THEN** only `worker-baseline` and `worker-hierarchical` SHALL start, and messages published to the study queues SHALL wait in them

### Requirement: Admin Bulk Enqueue API
The API SHALL expose, under `/api/ingestion` and the `Policies.PlatformAdmin` policy (role `admin-plataforma`), two bulk endpoints that take an optional `pipelineMode` query parameter (`BASELINE`, `HIERARCHICAL`, `RECURSIVE`, `SEMANTIC`, a comma-separated combination of them, `BOTH` for baseline and hierarchical, or `ALL` for the four; no value means `BOTH`) and answer `{ message, enqueued, failed }`:
- `POST /api/ingestion/reprocess-all`: lists every blob under `json/` in the bucket and enqueues them in batches of 1,000;
- `POST /api/ingestion/enqueue-bulk`: enqueues the body, a list of `{ blobPath, source }`.

A `pipelineMode` with an unknown name, alone or inside a comma-separated list, SHALL be rejected with `400 Bad Request` (a validation problem naming the unknown value) and nothing SHALL be enqueued; it SHALL NOT fall back to `BOTH`.

Each document SHALL be published once per selected pipeline as a `DocumentMessage` (`source`, `document_id`, `blob_path`), with the document id taken from the blob file name and, for `reprocess-all`, the source taken from the `json/{SOURCE}/` path segment. The admin documents UI SHALL offer the same options.

#### Scenario: Reprocessing every stored document in both pipelines
- **WHEN** a platform administrator calls `POST /api/ingestion/reprocess-all?pipelineMode=BOTH`
- **THEN** every blob under `json/` SHALL be published to `documents_to_process_baseline` and to `documents_to_process_hierarchical`
- **AND** the response SHALL report the number of documents enqueued and the number whose publish failed

#### Scenario: Enqueuing the study corpus for the four variants
- **WHEN** a platform administrator calls `POST /api/ingestion/enqueue-bulk?pipelineMode=ALL` with the corpus list
- **THEN** each document SHALL be published to the four queues and its four pipeline statuses set to `Queued`

#### Scenario: Enqueuing selected documents for one pipeline
- **WHEN** a platform administrator calls `POST /api/ingestion/enqueue-bulk?pipelineMode=HIERARCHICAL` with `[{ "blobPath": "json/BOE/BOE-A-2026-1234.json", "source": "BOE" }]`
- **THEN** a `DocumentMessage` for `BOE-A-2026-1234` SHALL be published only to `documents_to_process_hierarchical`
- **AND** only the hierarchical status of that document SHALL change to `Queued`

#### Scenario: Unknown pipeline name
- **WHEN** a platform administrator calls `POST /api/ingestion/reprocess-all?pipelineMode=RECURSIVE,FIXED`
- **THEN** the API SHALL answer `400 Bad Request` with a validation problem for `pipelineMode` that names `FIXED`
- **AND** nothing SHALL be published to any queue

#### Scenario: Empty bucket
- **WHEN** `reprocess-all` finds no blob under `json/`
- **THEN** it SHALL answer 200 with `enqueued = 0` and `failed = 0`

#### Scenario: Caller without the platform administrator role
- **WHEN** an authenticated user without `admin-plataforma` calls either endpoint, with any `pipelineMode`
- **THEN** the API SHALL answer `403 Forbidden` and enqueue nothing

### Requirement: Admin Documents UI
The Angular documents page SHALL list the stored blobs page by page (`GET /api/ingestion/blobs`), let the administrator choose the pipeline mode (`BOTH` by default), select documents and enqueue them, and trigger the reprocessing of every stored document after a confirmation dialog.

#### Scenario: Select all
- **WHEN** the administrator checks the "select all" checkbox of the table
- **THEN** the documents of the current page SHALL be added to the selection (documents of other pages are not selected)

#### Scenario: Filtering the list
- **WHEN** the administrator types a search text, chooses a status tab or sets a date range or a size range
- **THEN** the list SHALL show only the blobs whose path contains the text (case-insensitive), whose status matches the tab, whose `LastModified` date falls in the range and whose size falls in the range
- **AND** the filters SHALL be kept in the browser's `localStorage`

#### Scenario: Following the progress
- **WHEN** documents have been enqueued
- **THEN** the page SHALL show the counters returned in `stats` by `GET /api/ingestion/blobs` (total, pending, queued, processing, completed)
- **AND** update the rows and the counters live from the SignalR `DocumentStatusChanged` and `DocumentStatusesChanged` events

### Requirement: Reprocessing Idempotency
Ingesting a document that was already ingested SHALL replace its previous data in the pipeline that processes it, and SHALL add a new `IngestionMetrics` row instead of overwriting the previous ones.

#### Scenario: Re-ingesting an already-processed document
- **WHEN** a document already ingested by both pipelines is enqueued again with `pipelineMode=BOTH`
- **THEN** the baseline pipeline SHALL delete its `DocumentChunks` rows and Qdrant points and insert the new ones
- **AND** the hierarchical pipeline SHALL delete its `ParentDocument` (its `ChildFragments` cascade) and Qdrant points and insert the new ones
- **AND** each pipeline SHALL add one `IngestionMetrics` row, keeping the earlier rows for comparison

