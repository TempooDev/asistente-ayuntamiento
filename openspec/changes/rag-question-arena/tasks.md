## 1. Database Schema and Baseline Preservation

- [x] 1.1 Rename the existing `DocumentChunks` table to `chunks_baseline_v1` via EF Core migration (or raw SQL script) to preserve the baseline dataset for ablation studies. *(Note: Cancelled per user request, table remains DocumentChunks)*
  - Files: `AsistenteAyuntamiento.Infrastructure/Migrations/`, `AsistenteAyuntamiento.Infrastructure/Data/AppDbContext.cs`
- [x] 1.2 Create EF Core entity `ParentDocument` with all fields (Bulletin, DocumentId, NormativeRank, IssuingBody, NormTitle, NormSection, Municipality, FullText, PublicationDate, IsActive, Metadata JSONB, CreatedAt). Table: `ParentDocuments` in schema `ingestion`.
  - Files: `AsistenteAyuntamiento.Domain/Features/Ingestion/ParentDocument.cs`, `AsistenteAyuntamiento.Infrastructure/Data/Configurations/ParentDocumentConfiguration.cs`
- [x] 1.3 Create EF Core entity `ChildFragment` with pgvector `VECTOR(1536)` column, `TSVECTOR` column, and FK to `ParentDocument`. Configure HNSW index on Embedding and GIN index on TsvContent. Add the Spanish tsvector trigger via raw SQL in the migration. Table: `ChildFragments` in schema `ingestion`.
  - Files: `AsistenteAyuntamiento.Domain/Features/Ingestion/ChildFragment.cs`, `AsistenteAyuntamiento.Infrastructure/Data/Configurations/ChildFragmentConfiguration.cs`
- [x] 1.4 Create EF Core entity `ArenaBattle` with all arena fields (SessionId, UserQuery, LeftSystem/RightSystem, LeftResponse/RightResponse, LeftLatencyMs/RightLatencyMs, Winner, ClarityReason, PrecisionReason, OptionalComment). Table: `ArenaBattles` in schema `arena`.
  - Files: `AsistenteAyuntamiento.Domain/Features/Arena/ArenaBattle.cs`, `AsistenteAyuntamiento.Infrastructure/Data/Configurations/ArenaBattleConfiguration.cs`
- [x] 1.5 Create EF Core entity `IngestionMetric` with fields (Pipeline, Bulletin, DocumentId, TotalTokensEmbedded, TotalLlmCalls, TotalLlmTokens, ProcessingDurationMs, ChunksGenerated). Table: `IngestionMetrics` in schema `ingestion`.
  - Files: `AsistenteAyuntamiento.Domain/Features/Ingestion/IngestionMetric.cs`, `AsistenteAyuntamiento.Infrastructure/Data/Configurations/IngestionMetricConfiguration.cs`
- [x] 1.6 Register all new entities in `AppDbContext`, generate and apply the EF Core migration.
  - Files: `AsistenteAyuntamiento.Infrastructure/Data/AppDbContext.cs`, `AsistenteAyuntamiento.Infrastructure/Migrations/`

## 2. Hierarchical Ingestion Pipeline

- [ ] 2.1 Develop `BoeIngestionService.cs` as a .NET Background Service. Parse BOE XML (Legislación Consolidada API / XML summaries) using `XDocument`. Extract each `<articulo>` or disposition block as a parent document. Split numbered sub-sections (`1.`, `2.`, `a)`, `b)`) into child fragments of 250–400 tokens.
  - Files: `AsistenteAyuntamiento.Worker/Services/BoeIngestionService.cs`
- [ ] 2.2 Develop `BojaIngestionService.cs` as a .NET Background Service. Consume the Junta de Andalucía Open Data JSON API using `HttpClient` + `System.Text.Json`. Decompose convocatorias and regulatory bases into parent (per article/requirement) and child fragments.
  - Files: `AsistenteAyuntamiento.Worker/Services/BojaIngestionService.cs`
- [ ] 2.3 Implement the child fragment enrichment step: prepend the contextual breadcrumb (`[BOLETÍN: ... | ORGANISMO: ... | NORMA: ... | ARTÍCULO: ...]`) and generate two synthetic citizen questions per fragment via a fast LLM call through Semantic Kernel. Concatenate breadcrumb + questions + body text into `ChunkText`.
  - Files: `AsistenteAyuntamiento.Worker/Services/FragmentEnrichmentService.cs`
- [ ] 2.4 Integrate `IngestionMetricsService.cs` into both ingestion services. Wrap each document processing run with `Stopwatch`, count tokens embedded and LLM calls made, and persist an `IngestionMetric` record upon completion.
  - Files: `AsistenteAyuntamiento.Worker/Services/IngestionMetricsService.cs`

## 3. Hybrid Retrieval Service

- [ ] 3.1 Implement `QueryExpansionService.cs`. Accept the citizen's plain-language query. Call the LLM (via Semantic Kernel) with a prompt that returns a structured object containing `query_lexica` (tsquery-compatible terms), `query_semantica` (formal expanded phrase), and `filtro_municipio` (detected municipality or null).
  - Files: `AsistenteAyuntamiento.Application/Services/QueryExpansionService.cs`
- [ ] 3.2 Implement `HybridRetrievalService.cs`. Execute the RRF SQL query (dense HNSW + sparse GIN) using `SqlQueryRaw<T>()` or Dapper. Accept the expanded queries and municipality filter as parameters. Return the top 5 child IDs with their parent IDs and RRF scores.
  - Files: `AsistenteAyuntamiento.Application/Services/HybridRetrievalService.cs`
- [ ] 3.3 Implement parent resolution logic within `HybridRetrievalService`. Given the distinct `ParentId` values from the RRF results, fetch the corresponding `ParentDocuments.FullText` records. Pass the full parent texts to the generation service.
  - Files: `AsistenteAyuntamiento.Application/Services/HybridRetrievalService.cs`

## 4. Clear-Language Generation Service

- [ ] 4.1 Implement `ClearLanguageGenerationService.cs`. Construct the system prompt (citizen-friendly, structured headings, jargon explanation, source citation). Inject the resolved parent texts as context. Call the LLM via Semantic Kernel and return the structured response.
  - Files: `AsistenteAyuntamiento.Application/Services/ClearLanguageGenerationService.cs`

## 5. Question Arena Backend

- [ ] 5.1 Implement `ArenaCompareEndpoint` (`POST /api/arena/compare`). Accept the user query. Execute both pipelines concurrently using `Task.WhenAll`: (A) baseline vector search on `chunks_baseline_v1` + direct prompt, (B) query expansion + hybrid RRF + parent resolution + clear-language generation. Randomize left/right assignment (50/50). Return session_id, option_alfa, option_beta, and latencies.
  - Files: `AsistenteAyuntamiento.ApiService/Endpoints/ArenaEndpoints.cs`, `AsistenteAyuntamiento.Application/Services/ArenaService.cs`
- [ ] 5.2 Implement `ArenaVoteEndpoint` (`POST /api/arena/vote`). Accept SessionId, Winner, ClarityReason, PrecisionReason, and OptionalComment. De-randomize the left/right mapping and persist the `ArenaBattle` record.
  - Files: `AsistenteAyuntamiento.ApiService/Endpoints/ArenaEndpoints.cs`

## 6. Question Arena Frontend

- [ ] 6.1 Create the Angular Arena page component with a query input box and "Compare Responses" button. Display two response columns labeled "Assistant Alfa" and "Assistant Beta" with loading spinners during concurrent execution.
  - Files: `AsistenteAyuntamiento.Angular/src/app/features/arena/`
- [ ] 6.2 Add voting UI: preference buttons (Prefer Alfa / Prefer Beta / Technical Tie / Both Deficient), single-click sub-questions (clarity and precision), and an optional free-text comment field.
  - Files: `AsistenteAyuntamiento.Angular/src/app/features/arena/`
- [ ] 6.3 After vote submission, reveal which architecture powered each assistant. Show a collapsible panel with the actual source articles used by each pipeline.
  - Files: `AsistenteAyuntamiento.Angular/src/app/features/arena/`

## 7. Metrics, Analysis, and Export

- [ ] 7.1 Implement Flesch-Szigriszt Index (IFSZ) calculation in C# (`ReadabilityService.cs`). Count syllables (Spanish rules), words, and sentences. Apply the IFSZ formula: `206.835 - 62.3 * (syllables/words) - (words/sentences)`. Compare raw gazette text vs. generated responses from both pipelines.
  - Files: `AsistenteAyuntamiento.Application/Services/ReadabilityService.cs`
- [ ] 7.2 Implement win-rate aggregation and statistical significance testing. Query `ArenaBattles` for win counts per system. Implement a binomial test (equivalent to `scipy.stats.binomtest`) to determine if the new system's win rate is statistically significant (p < 0.05). Build a criteria matrix correlating overall winner with clarity and precision sub-votes.
  - Files: `AsistenteAyuntamiento.Application/Services/ArenaAnalyticsService.cs`
- [ ] 7.3 Implement `MetricsExportService.cs` to generate CSV/JSON reports with: win rates, p-values, IFSZ scores (per pipeline), cost comparison (tokens embedded, LLM calls, latency), suitable for direct inclusion in the TFG thesis.
  - Files: `AsistenteAyuntamiento.Application/Services/MetricsExportService.cs`

## 8. Admin Dashboard

- [ ] 8.1 Create secured Admin API endpoints (`[Authorize(Roles = "Admin")]`) to serve aggregated metrics: ingestion cost comparison (tokens/latency per pipeline), arena win rates, and IFSZ score distributions.
  - Files: `AsistenteAyuntamiento.ApiService/Endpoints/AdminMetricsEndpoints.cs`
- [ ] 8.2 Create the Angular Admin Dashboard page (route-guarded) displaying: token cost comparison chart, latency comparison chart, arena win-rate visualization with significance indicator, and IFSZ score comparison.
  - Files: `AsistenteAyuntamiento.Angular/src/app/features/admin/metrics/`
- [ ] 8.3 Configure custom OpenTelemetry Meters in the ingestion and retrieval services for real-time visibility in the .NET Aspire Dashboard (tokens_embedded counter, query_latency histogram, arena_votes counter).
  - Files: `AsistenteAyuntamiento.Worker/Services/`, `AsistenteAyuntamiento.Application/Services/`

## 9. Dual Worker Deployment and Bulk Reprocessing

- [ ] 9.1 Add `WORKER_PIPELINE_MODE` environment variable support to the Worker's `Program.cs`. When set to `BASELINE`, register only the existing flat-chunk ingestion service consuming from `documents_to_process_baseline` queue. When set to `HIERARCHICAL`, register only the new hierarchical ingestion services consuming from `documents_to_process_hierarchical` queue. Default (unset) preserves current behavior for backward compatibility.
  - Files: `AsistenteAyuntamiento.Worker/Program.cs`, `AsistenteAyuntamiento.Worker/appsettings.json`
- [ ] 9.2 Add `worker-baseline` and `worker-hierarchical` service definitions to `docker-compose.yml` (same image, different `WORKER_PIPELINE_MODE` and `WORKER_QUEUE_NAME` env vars). Add corresponding resource definitions in .NET Aspire `AppHost/Program.cs` for local development.
  - Files: `docker-compose.yml`, `AsistenteAyuntamiento.AppHost/Program.cs`
- [ ] 9.3 Declare the two new RabbitMQ queues (`documents_to_process_baseline`, `documents_to_process_hierarchical`) in the Worker startup and ensure idempotent `QueueDeclare` on both workers.
  - Files: `AsistenteAyuntamiento.Worker/Program.cs`
- [ ] 9.4 Implement `POST /api/admin/reprocess` endpoint (Admin-authorized). Accept `pipeline_mode` (`BASELINE`, `HIERARCHICAL`, `BOTH`), `document_ids` (array or `"ALL"`), and optional gazette/date filters. List matching S3 blobs and publish `DocumentMessage` to the appropriate RabbitMQ queue(s). Return enqueued counts.
  - Files: `AsistenteAyuntamiento.ApiService/Endpoints/AdminReprocessingEndpoints.cs`
- [ ] 9.5 Create the Angular Admin Reprocessing page (`/admin/reprocessing`, route-guarded). Include: pipeline mode selector (radio: Baseline / Hierarchical / Both), document table with multi-select and "Select All" checkbox, gazette and date-range filters, "Start Reprocessing" button with confirmation dialog and progress feedback (enqueued count).
  - Files: `AsistenteAyuntamiento.Angular/src/app/features/admin/reprocessing/`
- [ ] 9.6 Add a `GET /api/admin/reprocessing/status` endpoint to report per-pipeline processing progress (documents enqueued vs. documents with `IngestionMetrics` records) so the admin UI can show a progress bar.
  - Files: `AsistenteAyuntamiento.ApiService/Endpoints/AdminReprocessingEndpoints.cs`

