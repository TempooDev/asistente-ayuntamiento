# Specification: Ingestion Administration Service

## Purpose
Keep the administrative ingestion endpoints (`/api/ingestion/*`) thin by putting blob listing, job-state maintenance and bulk enqueueing in the application service `IIngestionAdminService` (`AsistenteAyuntamiento.Application.Features.Ingestion`), which works through ports (`IBlobStorage`, `IIngestionQueuePublisher`, `IVectorStore`, `IIngestionMaintenance`, `INotificationService`) instead of S3, RabbitMQ or Qdrant clients.
## Requirements
### Requirement: Platform Administrator Endpoints
Every endpoint under `/api/ingestion` SHALL require the `Policies.PlatformAdmin` policy (role `admin-plataforma`): `GET /blobs`, `POST /process-blob`, `POST /enqueue-bulk`, `POST /reprocess-all`, `POST /reset-status/{documentId}`, `POST /reset-stuck-processing` and `POST /reset`.

#### Scenario: Organization administrator calls an ingestion endpoint
- **WHEN** a user with only `admin-organizacion` calls `GET /api/ingestion/blobs`
- **THEN** the API SHALL answer `403 Forbidden`

### Requirement: Blob Listing and Inspection
`ListBlobsAsync` SHALL list the blobs under `json/` combined with their `DocumentJobState`: the summary `Status` (`Pending` when there is none) and the status of each pipeline (`BaselineStatus`, `HierarchicalStatus`, null when that pipeline never received the document) with the last error. It SHALL cache that combined list in memory for 10 seconds, and filter and paginate it in memory. The documents page SHALL show one progress bar per pipeline (completed, processing, queued, failed and not sent, out of the total documents), one status column per pipeline in the table with the error as a tooltip, and SHALL refresh the counters and the visible page every 10 seconds while any document is queued or processing in some pipeline. The status filter SHALL default to "Todos".

#### Scenario: Listing documents with filters
- **WHEN** an administrator calls `GET /api/ingestion/blobs` with `search`, `status`, `pipeline`, `dateFrom`, `dateTo`, `minSizeKb`, `maxSizeKb`, `page` and `pageSize`
- **THEN** the result SHALL contain the blobs whose path contains `search` (case-insensitive), whose status matches the tab (`Procesados` = Completed; `Pendientes` = Pending, Failed or VectorSyncFailed; `Encolados` = Queued or Processing; `Todos` or empty = all), evaluated on the given `pipeline` (`BASELINE` or `HIERARCHICAL`) or on the summary status when `pipeline` is empty, whose `LastModified` date is within the range and whose size is within the range
- **AND** return `total` (filtered count), `page`, `pageSize` (default 100) and the page of `items`, each with its summary status and its per-pipeline statuses

#### Scenario: Counters
- **WHEN** the list is returned
- **THEN** `stats` SHALL count all listed blobs (not only the filtered ones) as total, pending (Pending, Failed, VectorSyncFailed), queued, processing and completed on the summary status
- **AND** `pipelineStats` SHALL give the same counts for each pipeline on its own status, plus `failed` and `notSent` (no status for that pipeline)

#### Scenario: One pipeline ahead of the other
- **GIVEN** 201 documents, 150 completed by the baseline pipeline and 3 by the hierarchical one
- **WHEN** an administrator opens the documents page
- **THEN** the baseline bar SHALL show 150 of 201 completed and the hierarchical bar 3 of 201, instead of a single "queued" count for both

#### Scenario: Progress while ingesting
- **WHEN** some document is queued or processing in any pipeline
- **THEN** the page SHALL refresh the bars and the visible rows every 10 seconds, and SHALL stop refreshing when nothing is queued or processing

#### Scenario: Bucket unavailable
- **WHEN** listing the bucket fails
- **THEN** the error SHALL be logged and the blobs read so far returned (an empty list if none)

### Requirement: Bulk Queueing and Reprocessing
`EnqueueBulkAsync` and `ReprocessAllAsync` SHALL build one `DocumentMessage` per blob, set the requested pipelines' status to `Queued`, publish through `IIngestionQueuePublisher`, and notify connected administrators through `INotificationService` once per request batch. Only the requested pipelines' statuses change.

#### Scenario: Source inferred from the path
- **WHEN** a request item has no `source` (or an empty one)
- **THEN** the message SHALL use the source of the blob path (`json/{SOURCE}/{id}.json`), never a placeholder such as `Unknown`

#### Scenario: Bulk enqueueing blobs
- **WHEN** an administrator enqueues documents for `BASELINE`, `HIERARCHICAL` or `BOTH`
- **THEN** the service SHALL publish the messages to the selected queues, leave the documents `Queued` in the selected pipelines (or `Pending` if their publish failed), send `DocumentStatusesChanged` with the resulting summary statuses, and invalidate the blob-list cache

### Requirement: Manual Single-Document Processing
`POST /api/ingestion/process-blob` with `{ blobPath, source }` SHALL run the baseline ingestion (`IDocumentIngestionService.ProcessBlobAsync`) synchronously inside the API request. `source` is optional: when it is missing or empty, the source of the blob path MUST be used.

#### Scenario: Processing a blob without source
- **WHEN** an administrator posts `{ "blobPath": "json/BOJA/BOJA-2026-1-1.json" }`
- **THEN** the document SHALL be ingested as `BOJA`

#### Scenario: Processing one blob from the documents page
- **WHEN** an administrator processes `json/BOE/BOE-A-2026-1234.json` from the table
- **THEN** the API SHALL ingest it with the baseline pipeline and answer 200 when it finishes

### Requirement: Job State Maintenance
The service SHALL let administrators correct document job states: reset one document, repair documents stuck in `Processing`, and reset the baseline data. The summary `Status` SHALL only reflect the production pipelines (`BaselineStatus`, `HierarchicalStatus`); the study statuses (`RecursiveStatus`, `SemanticStatus`) are maintained per pipeline.

#### Scenario: Resetting one document
- **WHEN** an administrator calls `POST /api/ingestion/reset-status/{documentId}`
- **THEN** every pipeline status and the error SHALL be cleared and the summary set to `Pending`
- **AND** an unknown document SHALL answer `404 Not Found`

#### Scenario: Repairing stuck documents
- **WHEN** an administrator calls `POST /api/ingestion/reset-stuck-processing`
- **THEN** each pipeline left in `Processing`, including a study pipeline whose summary does not show it, SHALL become `Completed` if its data exists (`DocumentChunks` of the variant's `ChunkingStrategy` for a flat variant, a `ParentDocument` for hierarchical) and `Pending` otherwise
- **AND** the response SHALL report how many documents ended `Completed` and how many did not

#### Scenario: Resetting the baseline data
- **WHEN** an administrator calls `POST /api/ingestion/reset`
- **THEN** the service SHALL delete the `DocumentChunks` rows with `ChunkingStrategy = FixedOverlap`, drop the Qdrant collection `document_chunks` (recreated on the next ingestion) and invalidate the blob-list cache
- **AND** only the baseline job statuses SHALL be cleared with set-based updates (no row loaded): every `BaselineStatus` set to null, a `[Baseline]` error cleared, and the summary set to the hierarchical status, or `Pending` without one; rows with no other pipeline status SHALL be deleted
- **AND** the study variants' chunks, collections and statuses, and hierarchical data (`ParentDocuments`, `ChildFragments`, `child_fragments`), statuses and errors SHALL be left untouched

#### Scenario: Reset of a document ingested by both pipelines
- **WHEN** a document has `BaselineStatus = Completed` and `HierarchicalStatus = Failed` and the baseline data is reset
- **THEN** its row SHALL remain with `BaselineStatus = null`, `HierarchicalStatus = Failed`, summary `Failed` and its `[Hierarchical]` error

#### Scenario: Reset of a document only in a study variant besides the baseline
- **WHEN** a document has `BaselineStatus = Completed` and `SemanticStatus = Processing` and the baseline data is reset
- **THEN** its row SHALL remain with `BaselineStatus = null`, `SemanticStatus = Processing` and summary `Pending`

### Requirement: Documents Page Refresh Pauses When Nothing Moves
While some pipeline has documents queued or processing, the documents page SHALL refresh its counters every 10 seconds only as long as the per-pipeline counters keep changing: when a load returns exactly the same per-pipeline counters as the previous one, the periodic refresh SHALL pause, and it SHALL resume as soon as a later load (after a status notification, an action or a filter change) returns different counters.

#### Scenario: A study queue nobody consumes
- **WHEN** the production pipelines are complete and documents wait in a study queue with no study worker running
- **THEN** the page SHALL refresh once more and then stop polling, instead of polling forever

#### Scenario: Ingestion resumes
- **WHEN** the refresh is paused and a status notification or a reload returns different counters while something is still queued or processing
- **THEN** the page SHALL resume refreshing every 10 seconds

