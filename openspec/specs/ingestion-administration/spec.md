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
`ListBlobsAsync` SHALL list the blobs under `json/` combined with the summary `Status` of their `DocumentJobState` (`Pending` when there is none), cache that combined list in memory for 30 seconds, and filter and paginate it in memory.

#### Scenario: Listing documents with filters
- **WHEN** an administrator calls `GET /api/ingestion/blobs` with `search`, `status`, `dateFrom`, `dateTo`, `minSizeKb`, `maxSizeKb`, `page` and `pageSize`
- **THEN** the result SHALL contain the blobs whose path contains `search` (case-insensitive), whose status matches the tab (`Procesados` = Completed; `Pendientes` = Pending, Failed or VectorSyncFailed; `Encolados` = Queued or Processing; `Todos` or empty = all), whose `LastModified` date is within the range and whose size is within the range
- **AND** return `total` (filtered count), `page`, `pageSize` (default 100) and the page of `items`

#### Scenario: Counters
- **WHEN** the list is returned
- **THEN** `stats` SHALL count all listed blobs (not only the filtered ones) as total, pending (Pending, Failed, VectorSyncFailed), queued, processing and completed

#### Scenario: Bucket unavailable
- **WHEN** listing the bucket fails
- **THEN** the error SHALL be logged and the blobs read so far returned (an empty list if none)

### Requirement: Bulk Queueing and Reprocessing
`EnqueueBulkAsync` and `ReprocessAllAsync` SHALL build one `DocumentMessage` per blob, set the requested pipelines' status to `Queued`, publish through `IIngestionQueuePublisher`, and notify connected administrators through `INotificationService` once per request batch. Only the requested pipelines' statuses change.

#### Scenario: Bulk enqueueing blobs
- **WHEN** an administrator enqueues documents for `BASELINE`, `HIERARCHICAL` or `BOTH`
- **THEN** the service SHALL publish the messages to the selected queues, leave the documents `Queued` in the selected pipelines (or `Pending` if their publish failed), send `DocumentStatusesChanged` with the resulting summary statuses, and invalidate the blob-list cache

### Requirement: Manual Single-Document Processing
`POST /api/ingestion/process-blob` with `{ blobPath, source }` SHALL run the baseline ingestion (`IDocumentIngestionService.ProcessBlobAsync`) synchronously inside the API request.

#### Scenario: Processing one blob from the documents page
- **WHEN** an administrator processes `json/BOE/BOE-A-2026-1234.json` from the table
- **THEN** the API SHALL ingest it with the baseline pipeline and answer 200 when it finishes

### Requirement: Job State Maintenance
The service SHALL let administrators correct document job states: reset one document, repair documents stuck in `Processing`, and reset the baseline data.

#### Scenario: Resetting one document
- **WHEN** an administrator calls `POST /api/ingestion/reset-status/{documentId}`
- **THEN** both pipeline statuses and the error SHALL be cleared and the summary set to `Pending`
- **AND** an unknown document SHALL answer `404 Not Found`

#### Scenario: Repairing stuck documents
- **WHEN** an administrator calls `POST /api/ingestion/reset-stuck-processing`
- **THEN** each pipeline left in `Processing` SHALL become `Completed` if its data exists (`DocumentChunks` for baseline, a `ParentDocument` for hierarchical) and `Pending` otherwise
- **AND** the response SHALL report how many documents ended `Completed` and how many did not

#### Scenario: Resetting the baseline data
- **WHEN** an administrator calls `POST /api/ingestion/reset`
- **THEN** the service SHALL truncate `ingestion."DocumentChunks"` and `public."DocumentJobStates"`, drop the Qdrant collection `document_chunks` (recreated on the next ingestion) and invalidate the blob-list cache
- **AND** hierarchical data (`ParentDocuments`, `ChildFragments`, `child_fragments`) SHALL be left untouched
