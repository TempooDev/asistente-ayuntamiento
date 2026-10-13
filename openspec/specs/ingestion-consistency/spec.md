# ingestion-consistency Specification

## Purpose
Keep PostgreSQL, Qdrant and the job states consistent when documents are ingested or re-ingested: PostgreSQL is the source of truth and is committed before the vectors are written, writes are idempotent per document, no orphan vectors are left, each pipeline keeps its own job status, and bulk operations persist state before publishing.

## Requirements

### Requirement: Idempotent Re-Ingestion
Ingesting a document that was already ingested SHALL replace its previous representation in that pipeline's PostgreSQL tables and Qdrant collection, leaving no orphan rows or vectors. There SHALL be at most one `ParentDocument` per `DocumentId` (unique index), shared by no other pipeline.

#### Scenario: Reprocessing a document in the baseline pipeline
- **GIVEN** document `D1` ingested with 40 `DocumentChunks` rows and 40 points in `document_chunks`
- **WHEN** `D1` is reprocessed and now yields 35 chunks
- **THEN** PostgreSQL SHALL contain exactly 35 `DocumentChunks` rows for `D1`
- **AND** `document_chunks` SHALL contain exactly 35 points whose payload has `DocumentId = D1`

#### Scenario: Reprocessing a document in the hierarchical pipeline
- **WHEN** `D1` is reprocessed by the hierarchical pipeline
- **THEN** its previous `ParentDocument` SHALL be deleted (its `ChildFragments` by cascade) and the new one inserted in the same transaction
- **AND** after the commit, every `child_fragments` point whose payload has `DocumentId = D1`, or whose `ParentId` is one of the deleted parents (points written before the `DocumentId` payload existed), SHALL be deleted before the new points are upserted

### Requirement: Postgres as Source of Truth with Ordered Vector Sync
Ingestion SHALL commit the relational changes before writing vectors, and SHALL NOT write vectors inside an uncommitted transaction. If the vector write fails after the commit, the pipeline's job status SHALL become `VectorSyncFailed` and the message SHALL follow the retry policy; a retry re-runs the whole document idempotently.

#### Scenario: Qdrant unavailable after commit (hierarchical)
- **WHEN** the hierarchical transaction commits and the Qdrant write then fails
- **THEN** `BaseHierarchicalIngestionProcessor` SHALL throw `VectorSyncFailedException`
- **AND** `HierarchicalIngestionConsumer` SHALL set the hierarchical status to `VectorSyncFailed` with the Qdrant error and rethrow, so the message is retried
- **AND** the retry SHALL replace the PostgreSQL rows and re-sync the vectors without duplicates

#### Scenario: Qdrant unavailable after commit (baseline)
- **WHEN** the baseline transaction commits and the Qdrant write then fails
- **THEN** `DocumentIngestionService` SHALL set the baseline status to `VectorSyncFailed` and rethrow, so the message is retried

#### Scenario: Database commit fails
- **WHEN** the PostgreSQL commit fails
- **THEN** no vectors for that document SHALL have been written or deleted in Qdrant

### Requirement: Per-Pipeline Job State
`DocumentJobState` SHALL keep one status per pipeline (`BaselineStatus`, `HierarchicalStatus`; `null` = never requested) and a summary `Status` recomputed on every change: `Processing` if any requested pipeline is processing, else `Queued`, else `Failed`, else `VectorSyncFailed`, else `Pending`, else `Completed` when every requested pipeline completed. `ErrorMessage` SHALL hold the last error prefixed with its pipeline. Updates SHALL go through `DocumentJobStateUpdates.SetPipelineStatusAsync`, which uses PostgreSQL `xmin` as row version and retries a concurrent update or insert on fresh data (up to 4 attempts).

#### Scenario: Both workers finish the same document at the same time
- **WHEN** the baseline worker sets `Completed` while the hierarchical worker sets `Processing` on the same row
- **THEN** neither update SHALL be lost: the row SHALL end with `BaselineStatus = Completed`, `HierarchicalStatus = Processing` and summary `Processing`

#### Scenario: One pipeline fails
- **WHEN** the baseline pipeline completed and the hierarchical message is dead-lettered
- **THEN** the summary SHALL be `Failed` and `ErrorMessage` SHALL start with `[Hierarchical]`

### Requirement: State Saved Before Publish in Bulk Operations
Bulk enqueue and reprocess operations SHALL persist `Queued` for the requested pipelines before publishing, SHALL set those pipelines back to `Pending` (error "No se pudo encolar el documento.") for the documents whose publish failed, and SHALL emit the status notifications only after the final state is persisted.

#### Scenario: Partial publish failure
- **WHEN** 100 documents are enqueued and publishing fails for 10 of them
- **THEN** 90 documents SHALL be `Queued` and 10 SHALL be `Pending` in the requested pipelines
- **AND** the API response SHALL report `enqueued = 90` and `failed = 10`

### Requirement: Correct Document Metadata
Ingested documents SHALL carry the official publication date from the scraper's `metadata.fecha_publicacion`, parsed by `PublicationDateParser` (`yyyyMMdd`, `yyyy-MM-dd` or ISO 8601, as UTC). When the value is missing or cannot be parsed, the ingestion time SHALL be used (the hierarchical processors log a warning). Both pipelines SHALL read blobs from the configured bucket (`Blob:BucketName`, default `boletines`).

#### Scenario: BOE date format
- **WHEN** a BOE document carries `fecha_publicacion = 20260915`
- **THEN** its `PublicationDate` SHALL be 2026-09-15

#### Scenario: BOJA document publication date
- **WHEN** a BOJA document from the bulletin dated 2026-09-15 (`fecha_publicacion = 2026-09-15`) is ingested on 2026-10-13
- **THEN** its `PublicationDate` SHALL be 2026-09-15

#### Scenario: Unparsable date
- **WHEN** a document has no `fecha_publicacion` or an unknown format
- **THEN** its `PublicationDate` SHALL be the ingestion time

#### Scenario: Scraper JSON contract
- **WHEN** either pipeline reads a document written by the Go scraper
- **THEN** it SHALL read the title, department and publication date from `metadata.titulo`, `metadata.departamento` and `metadata.fecha_publicacion`, and the text from `text`

#### Scenario: Custom bucket name in production
- **GIVEN** `Blob__BucketName=boletines-prod` (set from `CLOUDFLARE_R2_BUCKET_NAME` in `docker-compose.yml`)
- **WHEN** a worker processes a message
- **THEN** it SHALL read the blob from `boletines-prod`

### Requirement: Null-Safe Optional Notifications
Ingestion services SHALL depend on a non-nullable `INotificationService`. Infrastructure SHALL register the no-op `NullNotificationService` unless the host registers a transport: the API registers `SignalRNotificationService` and the Worker registers `RabbitMqNotificationPublisher`.

#### Scenario: Host without a notification transport
- **WHEN** an ingestion service runs in a host that registers no notification transport
- **THEN** ingestion SHALL complete without a `NullReferenceException`, using `NullNotificationService`

#### Scenario: Worker notifications
- **WHEN** a Worker changes a document's status
- **THEN** it SHALL publish the document id and the summary status to the `document_notifications_exchange` fanout exchange, which the API relays to SignalR
