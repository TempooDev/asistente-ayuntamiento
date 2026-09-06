## 1. Infrastructure Setup

- [x] 1.1 Add Qdrant container configuration to `docker-compose.yml` and `deploy-dokploy.md`.
- [x] 1.2 Add Qdrant resource definition to `.NET Aspire` `AppHost` project for local development.

## 2. Core Service Implementation (SDKs)

- [x] 2.1 Add Qdrant SDK and Semantic Kernel Qdrant Connector NuGet packages to `AsistenteAyuntamiento.Infrastructure`.
- [x] 2.2 Configure Qdrant client dependency injection in `DependencyInjection.cs`.

## 3. Vector Data Migration (Primero Datos)

- [x] 3.1 Create a new API Endpoint or background task `MigrateToQdrantService` that queries PostgreSQL `ChildFragments`.
- [x] 3.2 Implement bulk insert logic in the migration service to map `ChildFragments` into Qdrant points with DocumentId and FragmentId as payload.
- [x] 3.3 Verify migration correctness (count records in Postgres vs Qdrant).

## 4. Connections & Refactoring (Después Conexiones)

- [x] 4.1 Update `HybridRetrievalService.cs` to query Qdrant for the dense vector semantic search.
- [x] 4.2 Update `HybridRetrievalService.cs` to execute the sparse search (GIN tsvector) against PostgreSQL.
- [x] 4.3 Implement in-memory Reciprocal Rank Fusion (RRF) in C# to combine the results from Qdrant and PostgreSQL.
- [ ] 4.4 Modify `AppDbContext.cs` to explicitly drop or stop querying the `Embedding` column from `ChildFragments` for dense search.
- [ ] 4.5 Update all corresponding unit/integration tests for the retrieval service.
