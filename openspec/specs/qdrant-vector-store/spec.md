# Specification: Qdrant Vector Store

## Purpose
Use Qdrant as the only vector database of the platform. pgvector indexes are limited to 2,000 dimensions, so dense embeddings (4,096 dimensions, Qwen3-Embedding-8B) are stored and searched in Qdrant. PostgreSQL remains the source of truth for relational data, text and full-text search. Archived change: `2026-10-13-migrate-embeddings-qdrant`.

## Requirements

### Requirement: Collections per Retrieval Pipeline
The system SHALL store the dense vectors of each retrieval pipeline in its own Qdrant collection, configured with an HNSW index and Cosine distance:
- `document_chunks`: baseline pipeline (fixed-overlap chunks of `DocumentChunks`);
- `child_fragments`: hierarchical pipeline (`ChildFragments`).

PostgreSQL SHALL NOT store embedding columns for these entities.

#### Scenario: Indexing a new child fragment
- **WHEN** the hierarchical ingestion processes a new child fragment with a 4096-dimensional embedding
- **THEN** the system SHALL upsert the vector into `child_fragments`, with a payload that contains at least `DocumentId` and `FragmentId`

#### Scenario: Indexing a baseline chunk
- **WHEN** the baseline ingestion processes a document chunk
- **THEN** the system SHALL upsert the vector into `document_chunks`, with a payload that contains at least `DocumentId`, the chunk identifier and the publication metadata used for citations

### Requirement: Collection Bootstrap
The system SHALL ensure, at startup of every service that writes vectors, that the required collections exist with the configured vector size and distance. Startup SHALL fail with a clear error if an existing collection has a different vector size.

#### Scenario: Fresh environment
- **GIVEN** an empty Qdrant instance (new developer machine, CI integration test, new deployment)
- **WHEN** the Worker or the ApiService starts
- **THEN** `document_chunks` and `child_fragments` SHALL be created with size `Ai:Embeddings:Dimensions` and Cosine distance
- **AND** the first ingestion SHALL succeed without manual steps

#### Scenario: Dimension mismatch
- **GIVEN** `child_fragments` exists with size 768 and the configured dimension is 4096
- **WHEN** the service starts
- **THEN** startup SHALL fail with an error that names the collection, the existing size and the expected size

### Requirement: Authenticated Access
Outside Development, Qdrant SHALL require an API key, and clients SHALL be configured with it. Qdrant SHALL NOT expose ports outside the internal network.

#### Scenario: Request without API key in production
- **WHEN** a client without the API key calls the production Qdrant instance
- **THEN** Qdrant SHALL reject the request with `401 Unauthorized`
