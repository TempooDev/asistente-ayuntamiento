# Proposal: Reorganize .NET Application Services by Feature, Establish Extensible Chunking Strategies for Comparative Evaluation, and Discard BOPMA Data Ingestion

## Why

The .NET backend is designed around Clean Architecture and Vertical Slice Architecture principles, where domain business logic and application use cases must reside in `AsistenteAyuntamiento.Application` organized by feature slices (`Features/<FeatureName>`). However, as the project evolved with the introduction of the hierarchical ingestion pipeline and dynamic scraper filtering, several architectural discrepancies emerged:

1. **Domain and business services trapped in host executable projects**:
   - `AsistenteAyuntamiento.Worker/Services` currently hosts `BoeIngestionService`, `BojaIngestionService`, `FragmentEnrichmentService`, `IngestionMetricsService`, and their respective interfaces (`IHierarchicalIngestionProcessor`, `IFragmentEnrichmentService`, `IIngestionMetricsService`). These services execute core domain business logic (synthetic question generation, embedding generation, fragment chunking, database persistence, and vector indexing), yet they are inaccessible to the API service or other components.
2. **Substantial code duplication across gazette ingestion pipelines**:
   - `BoeIngestionService` and `BojaIngestionService` share over 80% identical procedural boilerplate: calling LLM enrichment via `IFragmentEnrichmentService`, token/call accumulation, generating embeddings with `IEmbeddingGenerator`, instantiating and saving `ChildFragment` entities in `AppDbContext`, building gRPC point structs for `QdrantClient.UpsertAsync`, and emitting telemetry via `IngestionMetricsService`. The only difference between them is how the parent document and child sections are parsed from raw files (XML for BOE, JSON for BOJA).
3. **Strategic Foundation for Comparative Chunking Benchmark & Ablation Studies**:
   - The system is designed to evaluate and benchmark different chunking strategies (e.g., fixed-size sliding window with overlap, hierarchical parent-child decomposition, semantic boundary chunking, etc.) to determine empirically which yields superior legal retrieval precision, recall, and answer generation in testing suites and the Question Arena.
   - Preserving and modularizing the classic fixed-size overlapping chunking strategy alongside the hierarchical pipeline is essential so future chunking strategies can be plugged in without refactoring core storage, vector indexing, or message queueing.
4. **Infrastructure and data access logic leaked into API endpoints**:
   - `IngestionEndpoints.cs` in `ApiService` spans over 550 lines containing raw RabbitMQ connection and channel management (`IConnectionFactory`), S3/MinIO pagination and listing, database job status mutations, and raw SQL commands (`TRUNCATE`).
   - `ScraperFilterEndpoints.cs` and `FilterConfigServiceImpl.cs` execute direct queries and mutations against `IAppDbContext` without delegating to an Application layer service in `Application/Features/Scraper`.
5. **Clarification of gazette scope (discarding BOPMA)**:
   - There are legacy mentions of `BOPMA` (Boletín Oficial de la Provincia de Málaga) across enums and schemas. However, it has been decided to explicitly discard BOPMA data ingestion across all chunking and ingestion pipelines, focusing exclusively on BOE (national state) and BOJA (regional Andalusia).

Reorganizing these services into feature slices within `AsistenteAyuntamiento.Application`, creating an extensible chunking architecture, and abstracting shared ingestion logic will eliminate code duplication, prepare the foundation for empirical chunking research, and ensure architectural integrity.

## What Changes

- **Modular and Extensible Ingestion Architecture in `Application.Features.Ingestion`**:
  - **Pluggable Chunking Strategy Pattern**:
    - Introduce an `IChunkingStrategy` abstraction (or strategy delegates) allowing multiple chunking techniques (fixed token overlap, hierarchical structural, and future experimental strategies) to be plugged into the ingestion pipeline seamlessly for comparative testing.
    - Standardize the **Classic Baseline Chunking Strategy** (fixed-size sliding window with configurable `ChunkMaxLines`, `ChunkMaxTokens`, `ChunkOverlapTokens`) persisted into `DocumentChunks` and Qdrant (`document_chunks`).
  - **Hierarchical Pipeline Consolidation**:
    - Relocate `IHierarchicalIngestionProcessor`, `IFragmentEnrichmentService`, `FragmentEnrichmentService`, `IIngestionMetricsService`, and `IngestionMetricsService` to `AsistenteAyuntamiento.Application.Features.Ingestion`.
    - Introduce an abstract base class `BaseHierarchicalIngestionProcessor` (Template Method pattern) that encapsulates the common pipeline: synthetic enrichment, vector generation via `IEmbeddingGenerator`, PostgreSQL persistence (`ChildFragments`), Qdrant upserts (`child_fragments`), and telemetry recording.
    - Refactor `BoeIngestionService` and `BojaIngestionService` to inherit from `BaseHierarchicalIngestionProcessor`, keeping only their gazette-specific parsing logic (BOE from XML, BOJA from JSON).
- **Extract `IngestionAdminService` in `Application.Features.Ingestion`**:
  - Create `IIngestionAdminService` and `IngestionAdminService` to handle S3 blob listing with memory caching, RabbitMQ bulk enqueueing (`EnqueueBulk`), full reprocessing (`ReprocessAll`) targeting any pipeline mode, and state/vector reset operations, decoupling `IngestionEndpoints.cs`.
- **Create `ScraperFilterService` in `Application.Features.Scraper`**:
  - Implement `IScraperFilterService` and `ScraperFilterService` in `AsistenteAyuntamiento.Application.Features.Scraper` to handle CRUD operations and active rule queries for scraper filters.
  - Relocate `ScraperStateService` (in-memory scraper execution state) to `Application.Features.Scraper`.
  - Refactor `ScraperFilterEndpoints.cs` and `FilterConfigServiceImpl.cs` (gRPC) to consume `IScraperFilterService`.
- **Explicitly discard BOPMA data ingestion**:
  - Exclude BOPMA from all chunking and ingestion processors.
  - Document that the ingestion pipeline strictly supports BOE and BOJA.
  - Ensure that any incoming messages or requests specifying `BOPMA` are handled gracefully (logged with an informative warning and discarded/marked skipped without unhandled errors).
- **Clean up Host Projects (`Worker` and `ApiService`)**:
  - `Worker` and `ApiService` will register clean dependency injection extensions from `Application` and `Infrastructure`.
  - Host consumers (RabbitMQ background workers) will act as thin messaging adapters that delegate processing directly to scoped Application services (`IDocumentIngestionService` for baseline, keyed `IHierarchicalIngestionProcessor` for hierarchical).

## Capabilities

### New Capabilities
- `chunking-strategies`: Modular chunking abstraction in `Application.Features.Ingestion` designed to support comparative evaluation between fixed-size overlap chunking, hierarchical parent-child chunking, and future experimental strategies.
- `hierarchical-ingestion-core`: Unified abstract base in `Application` layer to enrich, vectorize, persist, and index hierarchical gazette documents (BOE and BOJA) without code duplication.
- `ingestion-administration`: Application service encapsulating S3 blob management, bulk queue dispatching for all pipeline modes, and status/vector resets.
- `scraper-filter-management`: Application service centralizing scraper dynamic filter queries and modifications.

### Modified Capabilities
- `worker-pipeline`: The Worker host acts as a thin execution environment hosting RabbitMQ consumers that delegate domain work to `Application` processors for baseline and hierarchical modes.
- `api-service-endpoints`: REST and gRPC endpoints delegate business operations to `Application` feature services.

## Impact

- **Affected Projects**:
  - `AsistenteAyuntamiento.Application`: Receives modular chunking strategies and relocated services in `Features/Ingestion` and `Features/Scraper`.
  - `AsistenteAyuntamiento.Worker`: Removes duplicate business logic, referencing `Application` services.
  - `AsistenteAyuntamiento.ApiService`: Decouples endpoints from direct database, S3, and RabbitMQ manipulations.
- **Evaluation & Research**:
  - Enables scientific, reproducible comparisons between different chunking strategies for the TFG thesis and Question Arena.
- **Dependencies**:
  - `Application` already has dependencies on `Qdrant.Client`, `AWSSDK.S3`, `Microsoft.SemanticKernel`, and `Microsoft.EntityFrameworkCore`.

## Non-goals

- No database migrations or schema modifications on PostgreSQL tables (`ParentDocuments`, `ChildFragments`, `DocumentChunks`, `DocumentJobStates`, etc.).
- No implementation of a BOPMA ingestion processor (expressly discarded from scope).
- No frontend changes in Angular or external scraper schema alterations.
