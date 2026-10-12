## ADDED Requirements

### Requirement: Idempotent Re-Ingestion
Ingesting a document that was already ingested SHALL replace its previous representation in both PostgreSQL and Qdrant, leaving no orphan rows or vectors and no duplicate `ParentDocument`.

#### Scenario: Reprocessing a document
- **GIVEN** document `D1` already ingested with 40 chunks in Postgres and 40 points in Qdrant
- **WHEN** `D1` is reprocessed and now yields 35 chunks
- **THEN** Postgres SHALL contain exactly 35 chunks for `D1`
- **AND** Qdrant SHALL contain exactly 35 points whose payload has `documentId = D1`
- **AND** there SHALL be exactly one `ParentDocument` per `(DocumentId, PipelineType)`

### Requirement: Postgres as Source of Truth with Ordered Vector Sync
Ingestion SHALL commit the relational changes before writing vectors. If the vector write fails after the commit, the job state SHALL become `VectorSyncFailed` and the message SHALL follow the retry policy. Vector writes SHALL NOT happen inside an uncommitted database transaction.

#### Scenario: Qdrant unavailable after commit
- **WHEN** the Postgres transaction commits and the Qdrant upsert then fails
- **THEN** the job state SHALL be `VectorSyncFailed`
- **AND** a retry SHALL delete and re-upsert the document's vectors without duplicating Postgres rows

#### Scenario: Database commit fails
- **WHEN** the Postgres commit fails
- **THEN** no new vectors for that document SHALL have been written to Qdrant

### Requirement: State Saved Before Publish in Bulk Operations
Bulk enqueue and reprocess operations SHALL persist the `Queued` job states before publishing messages. They SHALL revert to `Pending` the documents whose publish failed, and SHALL emit one aggregated notification per batch.

#### Scenario: Partial publish failure
- **WHEN** 100 documents are enqueued and publishing fails for 10 of them
- **THEN** 90 documents SHALL be `Queued` and 10 SHALL be `Pending`
- **AND** the API response SHALL report 90 enqueued and 10 failed

### Requirement: Correct Document Metadata
Ingested documents SHALL carry the official publication date parsed from the source, never the ingestion time. The hierarchical pipeline SHALL read from the configured bucket (`Blob:BucketName` / `CLOUDFLARE_R2_BUCKET_NAME`).

#### Scenario: BOE date format
- **WHEN** a BOE document carries `fecha_publicacion = 20260915`
- **THEN** its `PublicationDate` SHALL be 2026-09-15 (the `yyyyMMdd` format of the BOE XML SHALL be accepted)

#### Scenario: Scraper JSON contract
- **WHEN** the hierarchical pipeline reads a document written by the Go scraper
- **THEN** it SHALL read the title, department and publication date from `metadata.*` and the text from `text`

#### Scenario: BOJA document publication date
- **WHEN** a BOJA document from the bulletin dated 2026-09-15 is ingested on 2026-10-13
- **THEN** its `PublicationDate` SHALL be 2026-09-15

#### Scenario: Custom bucket name in production
- **GIVEN** `CLOUDFLARE_R2_BUCKET_NAME=boletines-prod`
- **WHEN** the hierarchical worker processes a message
- **THEN** it SHALL read the blob from `boletines-prod`

### Requirement: Null-Safe Optional Notifications
Ingestion services SHALL not fail when no notification service is available. They SHALL use a no-op `INotificationService` implementation instead of a nullable dependency.

#### Scenario: Worker without SignalR
- **WHEN** `DocumentIngestionService` runs in the Worker, where no notification transport is registered
- **THEN** ingestion SHALL complete without a `NullReferenceException`
