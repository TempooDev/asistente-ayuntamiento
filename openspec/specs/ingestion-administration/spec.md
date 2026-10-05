# Specification: Ingestion Administration Service

## Purpose
Decouple HTTP minimal API endpoints from direct infrastructure manipulation by introducing `IIngestionAdminService` in `AsistenteAyuntamiento.Application.Features.Ingestion`.

## Requirements

### Requirement: S3 Blob Listing and Inspection
The system SHALL provide an application service to list, search, and paginate blobs stored in object storage with in-memory caching to optimize response times.

#### Scenario: Listing documents with cache
- **WHEN** an administrator requests the list of ingested blobs
- **THEN** the system SHALL return cached S3 metadata combined with PostgreSQL `DocumentJobStates`
- **AND** support filtering by search terms, processing status, and date range.

### Requirement: Bulk Queueing and Reprocessing
The system SHALL encapsulate RabbitMQ queue declarations, batch publishing, and job state updates within the application service for baseline, hierarchical, and dual-pipeline reprocessing.

#### Scenario: Bulk enqueueing blobs
- **WHEN** an administrator enqueues a batch of documents for baseline, hierarchical, or both pipelines
- **THEN** `IngestionAdminService` SHALL publish corresponding messages to RabbitMQ, update `DocumentJobStates` to `Queued`, and notify connected clients via `INotificationService`.
