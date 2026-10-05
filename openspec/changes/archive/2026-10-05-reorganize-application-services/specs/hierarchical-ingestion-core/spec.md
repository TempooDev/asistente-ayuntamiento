# Specification: Hierarchical Ingestion Core in Application Layer

## Purpose
Consolidate hierarchical legal gazette ingestion into the Application layer (`AsistenteAyuntamiento.Application.Features.Ingestion`), sharing the common pipeline logic (synthetic enrichment, embedding generation, database persistence, Qdrant vector upsert, metrics tracking) through a reusable base processor, while explicitly discarding BOPMA data insertion.

## Requirements

### Requirement: Unified Hierarchical Ingestion Pipeline
The system SHALL provide a reusable abstract base processor (`BaseHierarchicalIngestionProcessor`) in the `Application` layer that executes the standard ingestion workflow for legal gazettes.

#### Scenario: Common pipeline execution
- **WHEN** a concrete gazette processor (BOE or BOJA) processes a legal document
- **THEN** it SHALL extract the parent entity and fragment list using gazette-specific parsers
- **AND** execute the common pipeline for synthetic enrichment via LLM, embedding generation, `ChildFragment` persistence in PostgreSQL, vector upsert in Qdrant, and telemetry metric recording in `IngestionMetrics`.

### Requirement: Discard BOPMA Data Ingestion
The ingestion pipeline SHALL explicitly discard and ignore data insertion requests for BOPMA (Boletín Oficial de la Provincia de Málaga).

#### Scenario: Ingestion request received for BOPMA
- **WHEN** a document with source `BOPMA` is received by the ingestion system
- **THEN** the system SHALL log an informative warning indicating that BOPMA data insertion is discarded
- **AND** mark the job state as skipped/unsupported without raising an unhandled exception or inserting records into the database.
