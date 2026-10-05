# Tasks: Reorganize .NET Application Services by Feature, Extensible Chunking Strategies, and Discard BOPMA Data Ingestion

## 1. Core Ingestion Refactoring (`Application/Features/Ingestion`)

- [ ] 1.1 Move hierarchical ingestion interfaces (`IHierarchicalIngestionProcessor`, `IFragmentEnrichmentService`, `IIngestionMetricsService`) to `AsistenteAyuntamiento.Application.Features.Ingestion`.
- [ ] 1.2 Move `FragmentEnrichmentService` and `IngestionMetricsService` to `AsistenteAyuntamiento.Application.Features.Ingestion`.
- [ ] 1.3 Create abstract class `BaseHierarchicalIngestionProcessor` in `Application/Features/Ingestion` encapsulating enrichment, embedding generation, PostgreSQL persistence, and Qdrant upserts.
- [ ] 1.4 Move and refactor `BoeIngestionService` and `BojaIngestionService` to `Application/Features/Ingestion` inheriting from `BaseHierarchicalIngestionProcessor`.
- [ ] 1.5 Introduce `IChunkingStrategy` abstraction and implement `FixedOverlapChunkingStrategy` (sliding window with configurable `ChunkMaxTokens` and `ChunkOverlapTokens`) in `Application/Features/Ingestion/Chunking` to support future comparative chunking evaluation.
- [ ] 1.6 Refactor `DocumentIngestionService` in `Application/Features/Ingestion` to utilize `FixedOverlapChunkingStrategy`.
- [ ] 1.7 Implement explicit discard handling for documents with source `BOPMA` across all ingestion pipelines (log informative warning and skip without database error).

## 2. Ingestion Administration Service (`Application/Features/Ingestion`)

- [ ] 2.1 Define `IIngestionAdminService` interface and request/response DTOs in `Application/Features/Ingestion`.
- [ ] 2.2 Implement `IngestionAdminService` with S3 blob listing and caching, bulk enqueueing (`EnqueueBulk`) targeting baseline and/or hierarchical queues, bulk reprocessing (`ReprocessAll`), and status/vector resets.
- [ ] 2.3 Refactor `IngestionEndpoints.cs` in `ApiService` to delegate operations to `IIngestionAdminService` and `IDocumentIngestionService`.

## 3. Scraper Feature Refactoring (`Application/Features/Scraper`)

- [ ] 3.1 Define `IScraperFilterService` interface and implement `ScraperFilterService` in `Application/Features/Scraper` to handle CRUD operations and active rule queries.
- [ ] 3.2 Move `ScraperStateService` to `Application/Features/Scraper`.
- [ ] 3.3 Refactor `ScraperFilterEndpoints.cs` in `ApiService` to consume `IScraperFilterService`.
- [ ] 3.4 Refactor `FilterConfigServiceImpl.cs` (gRPC) to consume `IScraperFilterService` instead of querying `IAppDbContext` directly.

## 4. DI Cleanup & Worker/Api Alignment

- [ ] 4.1 Update dependency injection in `AsistenteAyuntamiento.Worker/Program.cs` to resolve services from `Application` for both `BASELINE` (classic fixed chunking with overlap) and `HIERARCHICAL` modes.
- [ ] 4.2 Update dependency injection in `AsistenteAyuntamiento.ApiService/Program.cs` registering `IIngestionAdminService`, `IScraperFilterService`, and chunking strategies.
- [ ] 4.3 Remove obsolete files from `AsistenteAyuntamiento.Worker/Services` that have been moved to `Application`.

## 5. Verification & Tests

- [ ] 5.1 Add unit tests for `FixedOverlapChunkingStrategy` verifying line splitting and token overlap behavior.
- [ ] 5.2 Add unit tests for `ScraperFilterService` and new application services in `AsistenteAyuntamiento.Application.Tests`.
- [ ] 5.3 Verify both classic fixed chunking with overlap and hierarchical pipelines compile and pass with unit tests.
- [ ] 5.4 Run full test suite (`dotnet test`) and verify all tests compile and pass without regressions.
