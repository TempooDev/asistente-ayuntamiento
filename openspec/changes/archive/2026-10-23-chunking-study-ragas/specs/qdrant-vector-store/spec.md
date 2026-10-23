## MODIFIED Requirements

### Requirement: Collections per Retrieval Pipeline
The system SHALL store the vectors of each pipeline in its own Qdrant collection with Cosine distance: `document_chunks` for the baseline pipeline, `document_chunks_recursive` and `document_chunks_semantic` for the recursive and semantic flat-chunk variants (one point per `DocumentChunks` row of the variant's strategy), and `child_fragments` for the hierarchical pipeline (one point per `ChildFragments` row). The names come from `VectorStore:DocumentChunksCollection`, `VectorStore:RecursiveChunksCollection`, `VectorStore:SemanticChunksCollection` and `VectorStore:ChildFragmentsCollection`. Each point id SHALL be the id of its PostgreSQL row, and PostgreSQL SHALL NOT store embedding columns for these entities.

#### Scenario: Indexing a child fragment
- **WHEN** the hierarchical ingestion stores a child fragment
- **THEN** it SHALL upsert a point into `child_fragments` with id = `ChildFragments.Id` and payload `DocumentId`, `ParentId`, `Bulletin` (enum value), `Municipality` (the municipality match key, empty string when none), `SubSection` and `ChunkText`

#### Scenario: Indexing a baseline chunk
- **WHEN** the baseline ingestion stores a document chunk
- **THEN** it SHALL upsert a point into `document_chunks` with id = `DocumentChunks.Id` and payload `DocumentId`, `Source`, `Title`, `Department`, `Municipality` (the same match key as the hierarchical pipeline, empty string when none), `Content`, `ChunkIndex` and `PublicationDate` (ISO 8601), used by the baseline answer to cite its sources

#### Scenario: Indexing a flat chunk
- **WHEN** a flat-chunk variant stores a document chunk
- **THEN** it SHALL upsert a point into that variant's collection with id = `DocumentChunks.Id` and payload `DocumentId`, `Source`, `Title`, `Department`, `Municipality` (the same match key as the hierarchical pipeline, empty string when none), `Content`, `ChunkIndex` and `PublicationDate` (ISO 8601), used by the answer to cite its sources

#### Scenario: Deleting a document's vectors
- **WHEN** a document is re-ingested by one pipeline
- **THEN** that pipeline SHALL delete the points of its own collection whose `DocumentId` matches it (and, in `child_fragments`, those whose `ParentId` is one of its previous parents) before upserting the new ones
- **AND** the points of the other collections SHALL NOT change
