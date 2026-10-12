## ADDED Requirements

### Requirement: Qdrant Indexing for High-Dimensional Vectors
The system SHALL use Qdrant as the primary vector database for storing and indexing embeddings with dimensions greater than 2000 (e.g., 4096 dimensions from Qwen3).

#### Scenario: Indexing a new child fragment
- **WHEN** the ingestion service processes a new child fragment with a 4096-dimensional embedding
- **THEN** the system SHALL store the dense vector and its associated metadata (DocumentId, FragmentId) in a Qdrant collection configured with an HNSW index and Cosine distance metric.
