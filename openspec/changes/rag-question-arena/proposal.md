## Why

The current RAG pipeline uses a flat chunking strategy where documents are split into ~6,000 token blocks and embedded directly. This causes three problems: (1) retrieved chunks often include irrelevant surrounding text that dilutes the LLM context, (2) the system cannot combine lexical keyword matching with semantic vector search, and (3) generated answers reproduce legal jargon verbatim without adapting it for citizens. Additionally, there is no empirical way to measure whether architectural changes actually improve answer quality — we need a blind evaluation framework to collect human judgments for the TFG thesis.

## What Changes

- Introduce a **Parent-Child Retrieval** architecture: full legal articles are stored as "parent" documents, while fine-grained 250–400 token clauses become searchable "child" fragments enriched with contextual breadcrumbs and synthetic citizen questions.
- Implement **Hybrid Search** combining dense vector similarity (HNSW) with sparse full-text search (GIN/tsvector) fused via Reciprocal Rank Fusion (RRF).
- Add **Query Expansion** to translate plain citizen language into administrative terminology before searching.
- Build a **Generation Service** with a system prompt optimized for clear, jargon-free citizen communication.
- Deploy a **Question Arena** blind A/B testing system that runs both the baseline and new pipelines concurrently and collects structured human votes.
- Integrate **ingestion and retrieval telemetry** (token costs, embedding costs, latency) with an **Admin Dashboard** for monitoring.
- Add a **Bulk Reprocessing UI** in the admin panel to select which pipeline (baseline flat-chunk or hierarchical parent-child) and which documents (or all) to reprocess, covering the 6-month historical backlog.
- Deploy **two dedicated Worker instances** (one for baseline chunking, one for hierarchical ingestion) so both pipelines process the same documents under identical conditions, producing consistent and comparable telemetry data.

## Capabilities

### New Capabilities
- `hierarchical-retrieval`: Parent-Child document schema with enriched child fragments, hybrid search (dense + sparse RRF), query expansion, and parent-resolution for full-context generation.
- `question-arena`: Blind A/B testing system that runs baseline vs. new pipeline concurrently, randomizes presentation, and collects structured human votes with per-criterion breakdowns.
- `ingestion-telemetry`: Token consumption tracking, embedding cost calculation, and latency measurement for both ingestion pipelines, exposed via Admin Dashboard or .NET Aspire OpenTelemetry.
- `admin-metrics-dashboard`: Secured admin view to visualize win rates, IFSZ readability scores, cost comparisons, and latency charts.
- `bulk-reprocessing-pipeline`: Admin UI for selecting pipeline mode and documents to reprocess, dual RabbitMQ queue routing, and dual Worker deployment for parallel baseline/hierarchical ingestion of the 6-month historical backlog.

### Modified Capabilities
- `chunking-embeddings`: The existing `DocumentChunks` table is preserved as `chunks_baseline_v1` for ablation studies. New ingestion writes to `documentos_padre` / `fragmentos_hijo`.
- `query-api-semantic-kernel`: The retrieval path is extended to support hybrid search and query expansion alongside the existing pure vector search.

## Impact

- **Database (PostgreSQL)**: New tables (`documentos_padre`, `fragmentos_hijo`, `arena_battles`, `ingestion_metrics`), new HNSW and GIN indexes, tsvector trigger for Spanish full-text search.
- **Backend (.NET)**: New services (`BoeIngestionService`, `BojaIngestionService`, `RetrievalService`, `GenerationService`, `IngestionMetricsService`), new Arena API endpoints, new Admin API endpoints, new bulk reprocessing endpoint with pipeline mode selector.
- **Frontend (Angular)**: New Question Arena UI component, Admin Dashboard view with charts, and Admin Reprocessing panel with pipeline selector and document multi-select/select-all.
- **Infrastructure (Docker Compose / Aspire)**: Two Worker container instances deployed — `worker-baseline` consuming from `documents_to_process_baseline` queue and `worker-hierarchical` consuming from `documents_to_process_hierarchical` queue. Both share the same image but are configured via environment variable `WORKER_PIPELINE_MODE` (`BASELINE` or `HIERARCHICAL`).

## Non-goals

- PDF ingestion from BOPMA is deferred to a future phase.
- We will not replace the existing flat-chunk retrieval path — it remains operational as the baseline competitor in the Arena.
- The Admin Dashboard will not implement real-time streaming dashboards; periodic refresh or on-demand queries are sufficient.
- We will not build a custom statistical analysis UI — IFSZ and win-rate calculations export to CSV/JSON for integration into the TFG thesis document.
