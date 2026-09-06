## 1. Infrastructure Setup

- [ ] 1.1 Add Qdrant container configuration to `docker-compose.yml` and `deploy-dokploy.md`.
- [ ] 1.2 Add Qdrant resource definition to `.NET Aspire` `AppHost` project for local development.

## 2. Core Service Implementation

- [ ] 2.1 Add Qdrant SDK and Semantic Kernel Qdrant Connector NuGet packages to `AsistenteAyuntamiento.Infrastructure`.
- [ ] 2.2 Configure Qdrant client dependency injection in `DependencyInjection.cs`.
- [ ] 2.3 Modify `AppDbContext.cs` to explicitly drop or stop querying the `Embedding` column from `ChildFragments` for dense search (optionally keep it just for migration temporarily).

## 3. Vector Data Migration

- [ ] 3.1 Create a new API Endpoint or background task `MigrateToQdrantService` that queries PostgreSQL `ChildFragments`.
- [ ] 3.2 Implement bulk insert logic in the migration service to map `ChildFragments` into Qdrant points with DocumentId and FragmentId as payload.
- [ ] 3.3 Verify migration correctness (count records in Postgres vs Qdrant).

## 4. Hybrid Retrieval Refactoring

- [ ] 4.1 Update `HybridRetrievalService.cs` to query Qdrant for the dense vector semantic search.
- [ ] 4.2 Update `HybridRetrievalService.cs` to execute the sparse search (GIN tsvector) against PostgreSQL.
- [ ] 4.3 Implement in-memory Reciprocal Rank Fusion (RRF) in C# to combine the results from Qdrant and PostgreSQL.
- [ ] 4.4 Update all corresponding unit/integration tests for the retrieval service.
