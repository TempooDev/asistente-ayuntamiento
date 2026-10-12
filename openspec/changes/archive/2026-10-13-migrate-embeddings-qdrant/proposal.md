## Why

The hierarchical retrieval pipeline uses `qwen/qwen3-embedding-8b` which generates 4096-dimensional vectors. PostgreSQL's `pgvector` extension has a hard limit of 2,000 dimensions for its `hnsw` and `ivfflat` indexes. Currently, the hierarchical pipeline falls back to exact nearest-neighbor search (sequential scan) for these embeddings, which causes significant performance degradation. We need a vector database capable of indexing >2000 dimensions natively to restore sub-millisecond query performance, without having to pay for LLM/API usage to re-embed our existing documents.

## What Changes

- Introduce **Qdrant** as the primary vector database for the hierarchical retrieval pipeline.
- Create a data migration script to read existing 4096-dimensional embeddings and text chunks from PostgreSQL (`ChildFragments`) and insert them into Qdrant.
- Modify the `HybridRetrievalService` in the `.NET` application to query Qdrant (via Semantic Kernel Qdrant connector or Qdrant SDK) instead of PostgreSQL for vector search.
- Retain PostgreSQL for standard relational data (ChatSessions, Messages, ApiKeys) and potentially the baseline 768-dim embeddings if they remain performant, or migrate both to Qdrant.

## Capabilities

### New Capabilities
- `qdrant-vector-store`: Integration with Qdrant vector database for storing, indexing (HNSW), and querying high-dimensional (4096) embeddings.
- `vector-data-migration`: A one-off script or process to port existing embedding records from PostgreSQL to Qdrant without invoking the LLM API.

### Modified Capabilities
- `hybrid-retrieval`: The retrieval service will query Qdrant for semantic search instead of executing raw SQL against PostgreSQL.

## Impact

- **Dependencies**: Need to add Qdrant client SDK / Semantic Kernel Qdrant connector to the .NET backend.
- **Infrastructure**: Must provision a Qdrant instance (Docker container for self-hosted VPS) alongside PostgreSQL.
- **Cost**: Eliminates the need to reprocess embeddings (saving API costs).
- **Performance**: Significant reduction in chat latency for RAG queries relying on the hierarchical pipeline.

## Non-goals

- We will not change the embedding model (`qwen3-embedding-8b` or `gemini-embedding-004`).
- We will not migrate relational entities (Users, Chats, JobStates) out of PostgreSQL. Qdrant is strictly for vector semantic search.
