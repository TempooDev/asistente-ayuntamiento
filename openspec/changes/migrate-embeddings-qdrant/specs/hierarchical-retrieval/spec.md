## MODIFIED Requirements

### Requirement: Hybrid Search with Reciprocal Rank Fusion
The system SHALL combine dense vector similarity and sparse full-text search rankings to retrieve the most relevant child fragments.

#### Scenario: Executing a hybrid search
- **WHEN** an expanded query is submitted to the retrieval service
- **THEN** the system SHALL execute a dense ranking using Qdrant (cosine distance) and a sparse ranking using PostgreSQL (GIN tsvector `ts_rank`)
- **AND** fuse both rankings programmatically using RRF (`1/(k + rank)` with k=60) to produce a final ranked list of the top 5 child fragments.

#### Scenario: Filtering by municipality
- **WHEN** the query expansion detects a municipality filter
- **THEN** the hybrid search SHALL restrict results to fragments matching that municipality or fragments with no municipality (state/regional scope), applying this filter across both the Qdrant query and the PostgreSQL query.
