## MODIFIED Requirements

### Requirement: Pipeline Mode Configuration
The Worker SHALL read `WORKER_PIPELINE_MODE` at startup and register exactly one ingestion consumer: `HierarchicalIngestionConsumer` for `HIERARCHICAL`, the flat-chunk consumer of the `Recursive` variant for `RECURSIVE`, the one of the `Semantic` variant for `SEMANTIC` (all case-insensitive), and `BaselineIngestionConsumer` for `BASELINE`, for an unset variable, or for any value that is not a `PipelineType`. Each pipeline SHALL have its own queue with `.retry` and `.dlq` and the retry and dead-letter policy of `RabbitMqConsumerBase`, and its own status in `DocumentJobState` (`BaselineStatus`, `HierarchicalStatus`, `RecursiveStatus`, `SemanticStatus`; `null` = never requested), all included in the summary `Status`.

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
The API SHALL expose, under `/api/ingestion` and the `Policies.PlatformAdmin` policy (role `admin-plataforma`), two bulk endpoints that take an optional `pipelineMode` query parameter (`BASELINE`, `HIERARCHICAL`, `RECURSIVE`, `SEMANTIC`, a comma-separated combination of them, `BOTH` for baseline and hierarchical, or `ALL` for the four; anything else, or no value, means `BOTH`) and answer `{ message, enqueued, failed }`:
- `POST /api/ingestion/reprocess-all`: lists every blob under `json/` in the bucket and enqueues them in batches of 1,000;
- `POST /api/ingestion/enqueue-bulk`: enqueues the body, a list of `{ blobPath, source }`.

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

#### Scenario: Empty bucket
- **WHEN** `reprocess-all` finds no blob under `json/`
- **THEN** it SHALL answer 200 with `enqueued = 0` and `failed = 0`

#### Scenario: Caller without the platform administrator role
- **WHEN** an authenticated user without `admin-plataforma` calls either endpoint, with any `pipelineMode`
- **THEN** the API SHALL answer `403 Forbidden` and enqueue nothing
