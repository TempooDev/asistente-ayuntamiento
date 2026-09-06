## Context

The system utilizes high-dimensional embeddings (4096) from the `qwen/qwen3-embedding-8b` model to enable hierarchical retrieval for legal RAG workflows (BOE, BOJA). These embeddings are currently stored in a PostgreSQL database using the `pgvector` extension. However, `pgvector` imposes a hard limit of 2,000 dimensions for its optimized HNSW indexing algorithm, forcing the system to fall back to an unindexed sequential scan when searching across the `ChildFragments` table. 

This causes substantial and unacceptable latency spikes for end-users, scaling linearly with the size of the ingested dataset. Re-processing all documents to a lower-dimensional embedding model is cost-prohibitive.

## Goals / Non-Goals

**Goals:**
- Migrate 4096-dimensional embeddings from PostgreSQL to Qdrant.
- Ensure query latency returns to sub-millisecond ranges using HNSW index optimization provided natively by Qdrant for vectors >2000 dimensions.
- Preserve all existing embedding vectors without executing expensive LLM API calls.
- Integrate Qdrant seamlessly via the .NET Backend application.

**Non-Goals:**
- We are not changing the underlying text chunks, document processing flow, or embedding models themselves.
- We will not migrate other traditional relational data out of PostgreSQL (e.g. Chat sessions, APIs, DocumentJobStates).

## Decisions

1. **Adopt Qdrant as the Vector Database:**
   - **Rationale:** Qdrant is written in Rust, extremely resource-efficient (ideal for VPS deployments), natively supports HNSW indexes for up to 65,536 dimensions, and integrates smoothly with Semantic Kernel.
   - **Alternative Considered:** `Milvus` (too heavy for a VPS, requires multiple dependencies like MinIO, etcd) and `pgvecto.rs` (complex to swap extensions on standard managed DB setups).

2. **Run Qdrant as a separate Docker service:**
   - **Rationale:** Standard deployment will be added to the `.AppHost` project for local development and to the production `docker-compose.yml`.

3. **Data Migration Script Execution:**
   - **Rationale:** We will implement a custom C# console app or API endpoint triggered manually (`/api/admin/migrate-qdrant`) to read `ChildFragments` (Text + vector) from EF Core and insert them into Qdrant via `Qdrant.Client` or the Semantic Kernel `IQdrantVectorStore`.

## Risks / Trade-offs

- **Risk: Increased infrastructure footprint.** → *Mitigation:* Qdrant is very lightweight and can use disk-mmap storage, ensuring it does not consume excessive RAM on the VPS.
- **Risk: Network latency between DBs.** → *Mitigation:* Qdrant will be hosted on the same internal Docker network as the API Service and Postgres, keeping latency negligible.
- **Risk: Data consistency during migration.** → *Mitigation:* Pause ingestion queues (MassTransit) while the migration script runs to ensure no vectors are lost or duplicated.
