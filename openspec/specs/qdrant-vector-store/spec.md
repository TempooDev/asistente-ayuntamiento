# Specification: Qdrant Vector Store

## Purpose
Use Qdrant as the only vector database of the platform, behind the `IVectorStore` port (`QdrantVectorStore` in Infrastructure). pgvector indexes are limited to 2,000 dimensions and models such as Qwen3-Embedding-8B produce 4,096, so dense embeddings are stored and searched in Qdrant, while PostgreSQL remains the source of truth for relational data, text and full-text search. Archived change: `2026-10-13-migrate-embeddings-qdrant`.

## Requirements

### Requirement: Collections per Retrieval Pipeline
The system SHALL store the vectors of each pipeline in its own Qdrant collection with Cosine distance: `document_chunks` for the baseline pipeline (one point per `DocumentChunks` row) and `child_fragments` for the hierarchical pipeline (one point per `ChildFragments` row). The names come from `VectorStore:DocumentChunksCollection` and `VectorStore:ChildFragmentsCollection`. Each point id SHALL be the id of its PostgreSQL row, and PostgreSQL SHALL NOT store embedding columns for these entities.

#### Scenario: Indexing a child fragment
- **WHEN** the hierarchical ingestion stores a child fragment
- **THEN** it SHALL upsert a point into `child_fragments` with id = `ChildFragments.Id` and payload `DocumentId`, `ParentId`, `Bulletin` (enum value), `Municipality` (the municipality match key, empty string when none), `SubSection` and `ChunkText`

#### Scenario: Indexing a baseline chunk
- **WHEN** the baseline ingestion stores a document chunk
- **THEN** it SHALL upsert a point into `document_chunks` with id = `DocumentChunks.Id` and payload `DocumentId`, `Source`, `Title`, `Department`, `Municipality` (the same match key as the hierarchical pipeline, empty string when none), `Content`, `ChunkIndex` and `PublicationDate` (ISO 8601), used by the baseline answer to cite its sources

#### Scenario: Deleting a document's vectors
- **WHEN** a document is re-ingested
- **THEN** the pipeline SHALL delete the points whose `DocumentId` matches it (and, in `child_fragments`, those whose `ParentId` is one of its previous parents) before upserting the new ones

### Requirement: Collection Bootstrap
Before writing to a collection, the system SHALL ensure it exists with the vector size produced by the embedding model and Cosine distance, creating it when missing. If the collection exists with another vector size, the write SHALL fail with an error that names the collection, the existing size and the expected size. No manual setup or dimension setting is required.

#### Scenario: Fresh environment
- **GIVEN** an empty Qdrant instance
- **WHEN** the first document is ingested by each pipeline
- **THEN** `document_chunks` or `child_fragments` SHALL be created with the embedding size and Cosine distance
- **AND** the ingestion SHALL succeed without manual steps

#### Scenario: Dimension mismatch
- **GIVEN** `child_fragments` exists with size 768 and the embedding model produces 4096 dimensions
- **WHEN** a document is ingested
- **THEN** the vector sync SHALL fail with "Qdrant collection 'child_fragments' has vector size 768, but the embedding model produces 4096" and the document SHALL end `VectorSyncFailed` until retried or dead-lettered

#### Scenario: Resetting the baseline collection
- **WHEN** an administrator calls `POST /api/ingestion/reset`
- **THEN** `document_chunks` SHALL be dropped and recreated with the current embedding size on the next ingestion

### Requirement: Filtered Search
`IVectorStore.SearchAsync` SHALL return the nearest points of a collection, best first, up to a limit and above an optional score threshold. An optional text filter on a payload field SHALL keep the points whose field matches the text and the points where that field is empty, missing or null.

#### Scenario: Municipality filter
- **WHEN** the hierarchical retrieval searches `child_fragments` with the municipality "Málaga" (match key "malaga")
- **THEN** points whose `Municipality` contains "malaga" and points with an empty `Municipality` SHALL both be candidates
- **AND** the filter text SHALL match as a substring (no full-text index on the field), so stored keys and the query key are both normalized by `MunicipalityExtractor.ToMatchKey`

### Requirement: Authenticated Access
In the Production environment, the API and the Workers SHALL refuse to start unless `ConnectionStrings:qdrant` contains a non-empty `Key=` API key. The production compose file SHALL start Qdrant with `QDRANT__SERVICE__API_KEY` (required) and SHALL NOT publish Qdrant ports outside the internal Docker network. Outside Production (the Aspire AppHost) no key is enforced by the application.

#### Scenario: Production without API key
- **WHEN** a Worker starts in Production with `ConnectionStrings__qdrant=Endpoint=http://qdrant:6334`
- **THEN** it SHALL fail at startup with "ConnectionStrings:qdrant must include an API key (Key=...) in Production."

#### Scenario: Request without API key in production
- **WHEN** a client without the API key calls the production Qdrant instance
- **THEN** Qdrant SHALL reject the request
