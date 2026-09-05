# Specification: Bulk Reprocessing Pipeline with Dual Worker Deployment

## Purpose
Enable administrators to reprocess the 6-month historical document backlog through both ingestion pipelines (baseline flat-chunk and hierarchical parent-child) simultaneously, using dedicated Worker instances per pipeline to ensure fair, comparable telemetry data.

## ADDED Requirements

### Requirement: Pipeline Mode Configuration
The Worker service SHALL support a configurable pipeline mode that determines which ingestion strategy it executes.

#### Scenario: Starting in baseline mode
- **WHEN** the Worker starts with environment variable `WORKER_PIPELINE_MODE=BASELINE`
- **THEN** it SHALL register only the flat-chunk ingestion service
- **AND** consume messages exclusively from the `documents_to_process_baseline` RabbitMQ queue
- **AND** write results to the `chunks_baseline_v1` table.

#### Scenario: Starting in hierarchical mode
- **WHEN** the Worker starts with environment variable `WORKER_PIPELINE_MODE=HIERARCHICAL`
- **THEN** it SHALL register only the hierarchical ingestion services (BOE + BOJA parsers)
- **AND** consume messages exclusively from the `documents_to_process_hierarchical` RabbitMQ queue
- **AND** write results to the `ParentDocuments` and `ChildFragments` tables.

#### Scenario: Backward-compatible default mode
- **WHEN** the Worker starts without `WORKER_PIPELINE_MODE` set (or set to an unknown value)
- **THEN** it SHALL fall back to the current default behavior (consuming from `documents_to_process` queue with the existing ingestion logic).

### Requirement: Dual Worker Deployment
The system SHALL support deploying two Worker container instances from the same Docker image, each configured for a different pipeline mode.

#### Scenario: Parallel reprocessing
- **WHEN** both `worker-baseline` and `worker-hierarchical` containers are running
- **THEN** each SHALL consume from its own dedicated RabbitMQ queue without message contention
- **AND** both SHALL record `IngestionMetrics` entries tagged with their respective pipeline identifier.

### Requirement: Admin Bulk Reprocessing API
The system SHALL provide an admin-only API endpoint to enqueue documents for reprocessing through one or both pipelines.

#### Scenario: Enqueuing all documents for both pipelines
- **WHEN** an admin calls `POST /api/admin/reprocess` with `{ pipeline_mode: "BOTH", document_ids: "ALL" }`
- **THEN** the system SHALL list all JSON blobs in the S3 bucket
- **AND** publish a `DocumentMessage` to `documents_to_process_baseline` for each blob
- **AND** publish a `DocumentMessage` to `documents_to_process_hierarchical` for each blob
- **AND** return the count of messages enqueued per queue.

#### Scenario: Enqueuing selected documents for a single pipeline
- **WHEN** an admin calls `POST /api/admin/reprocess` with `{ pipeline_mode: "HIERARCHICAL", document_ids: ["BOE-A-2024-1234", "BOE-A-2024-5678"] }`
- **THEN** the system SHALL publish a `DocumentMessage` only to the `documents_to_process_hierarchical` queue for each specified document.

#### Scenario: Unauthorized reprocessing attempt
- **WHEN** a non-admin user calls `POST /api/admin/reprocess`
- **THEN** the system SHALL return HTTP 403 Forbidden.

### Requirement: Admin Reprocessing UI
The admin panel SHALL provide a visual interface for selecting pipeline mode, filtering documents, and triggering bulk reprocessing.

#### Scenario: Selecting all documents
- **WHEN** an admin checks the "Select All" checkbox in the reprocessing panel
- **THEN** all documents matching the current filters (gazette source, date range) SHALL be selected for reprocessing.

#### Scenario: Filtering by gazette and date range
- **WHEN** an admin filters by gazette `BOE` and date range `2024-01-01` to `2024-06-30`
- **THEN** only documents from that gazette and date range SHALL appear in the selection table.

#### Scenario: Monitoring reprocessing progress
- **WHEN** a reprocessing batch has been enqueued
- **THEN** the admin UI SHALL display a progress indicator showing documents processed vs. total enqueued (queried via `GET /api/admin/reprocessing/status`).

### Requirement: Reprocessing Idempotency
The system SHALL handle reprocessing of already-processed documents gracefully.

#### Scenario: Re-ingesting an already-processed document
- **WHEN** a document that was previously ingested is enqueued again
- **THEN** the ingestion service SHALL upsert (delete existing + re-insert) the corresponding parent/child records or baseline chunks
- **AND** create a new `IngestionMetrics` record (preserving historical metrics for comparison).
