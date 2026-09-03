## Context

The current retrieval pipeline performs flat vector search over ~6,000 token chunks stored in `DocumentChunks`. While functional, it lacks the precision of fine-grained retrieval and the recall benefits of lexical search. The system also generates answers that reproduce legal language verbatim, which is inaccessible to most citizens. There is no mechanism to empirically compare pipeline variants or measure the cost trade-offs of different chunking strategies.

## Goals / Non-Goals

**Goals:**
- Implement a Parent-Child document hierarchy where full legal articles (parents) provide generation context and fine-grained clauses (children) are the searchable units.
- Combine dense vector search (HNSW) with sparse full-text search (GIN tsvector) using Reciprocal Rank Fusion.
- Enrich child fragments with contextual breadcrumbs and LLM-generated synthetic citizen questions.
- Translate citizen queries into administrative terminology via Query Expansion before retrieval.
- Build a blind A/B testing arena to collect structured human judgments comparing baseline vs. new pipeline.
- Track ingestion costs (tokens, latency) for both strategies and expose metrics via an Admin Dashboard.

**Non-Goals:**
- We will not ingest BOPMA PDFs in this phase.
- We will not delete or modify the existing `DocumentChunks` data — it is preserved as `chunks_baseline_v1`.
- We will not replace the .NET Aspire dashboard — the Admin view complements it.

## Decisions

1. **Parent-Child Schema over Re-ranking:**
   - *Why:* Re-ranking (e.g., cross-encoder) adds latency and cost per query. Parent-Child retrieval achieves precision at the child level and full context at the parent level without per-query LLM overhead. The parent text is already structured by article/section in BOE/BOJA sources.
   - *Alternative considered:* Retrieve large chunks and re-rank with a cross-encoder. Rejected because it does not solve the jargon-to-plain-language translation problem and adds per-query cost.

2. **Hybrid Search with RRF over Pure Vector Search:**
   - *Why:* Legal documents contain exact terms (article numbers, law names, dates) that pure vector search handles poorly. Full-text search via tsvector/GIN captures these lexical signals. RRF merges both rankings without requiring learned weights.
   - *Alternative considered:* Separate keyword filter + vector search. RRF is more robust and does not require a hard filter that could eliminate relevant results.

3. **Query Expansion via LLM over Direct Embedding:**
   - *Why:* Citizens use colloquial language ("help for rent if I'm 24") while legal texts use administrative terms ("subvención vivienda jóvenes"). A fast LLM call translates the intent into both a lexical query and a formal semantic query, dramatically improving recall.
   - *Alternative considered:* Synonym dictionaries. Too brittle for the variety of citizen language.

4. **Blind A/B Arena over Automated Evaluation Only:**
   - *Why:* Automated metrics (IFSZ, BLEU) measure surface properties but not whether a citizen actually understood and found the answer useful. Human blind evaluation is the gold standard for the TFG thesis defense.
   - *Alternative considered:* LLM-as-judge evaluation. Added as a complementary metric but not a replacement for real human feedback.

5. **EF Core Raw SQL / Dapper for Hybrid Query over Pure EF Core LINQ:**
   - *Why:* The RRF query combines CTEs, window functions, full outer joins, and pgvector operators (`<=>`) with tsvector operators (`@@`, `ts_rank`). This is not expressible in EF Core LINQ. Raw SQL via `SqlQueryRaw<T>()` or Dapper provides full control.
   - *Alternative considered:* Two separate EF Core queries merged in C#. Rejected because it would require fetching more rows and doing fusion in application memory, increasing latency.

6. **Ingestion Cost Telemetry via `Stopwatch` + `IngestionMetrics` Table:**
   - *Why:* To produce the comparative cost analysis table for the TFG, we need to track: (a) number of tokens embedded per strategy, (b) number of LLM calls for enrichment/expansion, (c) wall-clock time per document. A dedicated `IngestionMetrics` entity provides queryable persistence; `Stopwatch` provides sub-millisecond timing. OpenTelemetry custom meters complement this for the Aspire dashboard.
   - *Alternative considered:* Rely solely on OpenTelemetry. Rejected because we need structured, queryable data for the thesis statistical analysis, not just time-series dashboards.

7. **Dual Worker Instances over Single Worker with Inline Branching:**
   - *Why:* To obtain fair, comparable telemetry data for the TFG, both pipelines must process the same 6-month document backlog under identical conditions (same hardware resources, same time window, no contention). Deploying two separate Worker containers from the same image — differentiated only by an environment variable `WORKER_PIPELINE_MODE` — ensures each pipeline runs independently on its own dedicated RabbitMQ queue without interfering with the other's throughput or latency measurements.
   - *Alternative considered:* A single Worker that processes each document twice (once per pipeline) sequentially. Rejected because sequential processing doubles wall-clock time and introduces ordering bias in latency measurements (the second pipeline benefits from OS-level caches warmed by the first).

8. **Admin Reprocessing UI with Pipeline Selector over CLI-Only Reprocessing:**
   - *Why:* The 6-month backlog contains thousands of documents. The admin needs to (a) choose which pipeline to target (baseline, hierarchical, or both), (b) select specific documents or select all, and (c) monitor enqueuing progress — all without SSH access to the server. A UI panel in the existing admin section provides this with minimal effort.
   - *Alternative considered:* CLI scripts or direct API calls via curl. Rejected because it requires server access and offers no visibility into which documents were already processed.

## Component Design

### Database Schema

```
documentos_padre
├── id (BIGSERIAL PK)
├── boletin (VARCHAR: 'BOE', 'BOJA')
├── doc_id (VARCHAR: e.g., 'BOE-A-2024-1234')
├── rango_normativo (VARCHAR: 'Ley', 'Real Decreto', 'Orden')
├── organo_emisor (TEXT)
├── titulo_norma (TEXT)
├── seccion_norma (VARCHAR: 'Artículo 12', 'Disposición Adicional 1')
├── municipio (VARCHAR, nullable)
├── texto_completo (TEXT: full article text)
├── fecha_publicacion (DATE)
├── vigente (BOOLEAN)
├── metadata (JSONB)
└── created_at (TIMESTAMPTZ)

fragmentos_hijo
├── id (BIGSERIAL PK)
├── parent_id (BIGINT FK → documentos_padre.id, CASCADE)
├── boletin (VARCHAR)
├── municipio (VARCHAR, nullable)
├── subseccion (VARCHAR: 'Apartado 1', 'Párrafo 2')
├── texto_chunk (TEXT: breadcrumb + synthetic questions + body)
├── tsv_content (TSVECTOR: auto-populated by trigger, Spanish config)
└── embedding (VECTOR(1536): HNSW index, cosine ops)

arena_battles
├── id (BIGSERIAL PK)
├── session_id (UUID)
├── query_usuario (TEXT)
├── sistema_izq / sistema_der (VARCHAR: 'BASELINE_6000' or 'NUEVO_HIBRIDO')
├── resp_izq / resp_der (TEXT)
├── latencia_izq_ms / latencia_der_ms (INTEGER)
├── vencedor (VARCHAR: 'IZQ', 'DER', 'EMPATE', 'AMBAS_MALAS')
├── motivo_claridad / motivo_precision (VARCHAR)
├── comentario_opcional (TEXT)
└── created_at (TIMESTAMPTZ)

ingestion_metrics
├── id (BIGSERIAL PK)
├── pipeline (VARCHAR: 'BASELINE_FLAT' or 'HIERARCHICAL')
├── boletin (VARCHAR)
├── doc_id (VARCHAR)
├── total_tokens_embedded (INTEGER)
├── total_llm_calls (INTEGER: enrichment calls)
├── total_llm_tokens (INTEGER: enrichment token usage)
├── processing_duration_ms (BIGINT)
├── chunks_generated (INTEGER)
└── created_at (TIMESTAMPTZ)
```

### Child Fragment Text Structure

Each `texto_chunk` in `fragmentos_hijo` follows a standardized enriched format:

```
[BOLETÍN: {boletin} | ORGANISMO: {organo_emisor} | NORMA: {titulo_norma} | ARTÍCULO: {seccion_norma} - {subseccion}]
[PREGUNTAS HABITUALES: {q1}? / {q2}?]
{body text of the legal clause}
```

### Hybrid Search SQL (RRF)

```sql
WITH params AS (
    SELECT $1::vector AS q_vec,
           websearch_to_tsquery('spanish', $2) AS q_txt,
           $3::varchar AS q_mun
),
dense_ranking AS (
    SELECT id, parent_id,
           ROW_NUMBER() OVER (ORDER BY embedding <=> (SELECT q_vec FROM params)) AS r_dense
    FROM fragmentos_hijo
    WHERE ((SELECT q_mun FROM params) IS NULL OR municipio = (SELECT q_mun FROM params) OR municipio IS NULL)
    LIMIT 20
),
sparse_ranking AS (
    SELECT id, parent_id,
           ROW_NUMBER() OVER (ORDER BY ts_rank(tsv_content, (SELECT q_txt FROM params)) DESC) AS r_sparse
    FROM fragmentos_hijo
    WHERE tsv_content @@ (SELECT q_txt FROM params)
      AND ((SELECT q_mun FROM params) IS NULL OR municipio = (SELECT q_mun FROM params) OR municipio IS NULL)
    LIMIT 20
)
SELECT COALESCE(d.id, s.id) AS child_id,
       COALESCE(d.parent_id, s.parent_id) AS parent_id,
       (COALESCE(1.0/(60 + d.r_dense), 0) + COALESCE(1.0/(60 + s.r_sparse), 0)) AS score_rrf
FROM dense_ranking d
FULL OUTER JOIN sparse_ranking s ON d.id = s.id
ORDER BY score_rrf DESC
LIMIT 5;
```

### Generation System Prompt

```
You are a municipal government assistant specializing in citizen services. Your goal is to explain official gazette regulations in a comprehensible, useful, and direct manner.

Style and content guidelines:
1. Answer the user's practical need first: what they must do, deadlines, and conditions.
2. Never use legal or administrative jargon without immediately explaining it (e.g., if you use "días hábiles", clarify that weekends and public holidays do not count).
3. Organize the response with clear headings: "Requirements", "Key Deadlines", "Documents to Provide", and "Where to Complete the Procedure".
4. At the end, include a standalone section titled "Official Sources Consulted" citing the exact gazette, regulation, and article.
5. If the fragments do not contain enough information for a confident answer, say so transparently and suggest visiting the citizen services office.
```

### Arena Concurrent Execution Flow

```
POST /api/arena/compare { query }
  ├─ Task A (Baseline): embed query → vector search on chunks_baseline_v1 → direct LLM prompt
  ├─ Task B (New): query expansion → hybrid RRF search → parent resolution → clear-language generation
  └─ Task.WhenAll(A, B)
       → Randomize left/right assignment (50/50)
       → Return { session_id, option_alfa, option_beta, latency_alfa_ms, latency_beta_ms }

POST /api/arena/vote { session_id, winner, motivo_claridad, motivo_precision, comentario }
  → Persist to arena_battles with de-randomized sistema_izq/sistema_der
```

### Dual Worker Deployment Architecture

Both workers share the same Docker image (`asistente-ayuntamiento-worker`). The pipeline mode is selected at container startup via environment variable:

```
docker-compose.yml (production additions):

  worker-baseline:
    image: ghcr.io/${GITHUB_USER}/asistente-ayuntamiento-worker:latest
    container_name: asistente-worker-baseline
    environment:
      - WORKER_PIPELINE_MODE=BASELINE
      - ConnectionStrings__messaging=amqp://...@rabbitmq:5672
      - WORKER_QUEUE_NAME=documents_to_process_baseline
      # ... same DB, blob, AI config as main worker

  worker-hierarchical:
    image: ghcr.io/${GITHUB_USER}/asistente-ayuntamiento-worker:latest
    container_name: asistente-worker-hierarchical
    environment:
      - WORKER_PIPELINE_MODE=HIERARCHICAL
      - ConnectionStrings__messaging=amqp://...@rabbitmq:5672
      - WORKER_QUEUE_NAME=documents_to_process_hierarchical
      # ... same DB, blob, AI config as main worker
```

The Worker's `Program.cs` reads `WORKER_PIPELINE_MODE` at startup and registers only the corresponding ingestion service:
- `BASELINE` → registers `FlatChunkIngestionService` (existing logic, writes to `chunks_baseline_v1`)
- `HIERARCHICAL` → registers `BoeIngestionService` + `BojaIngestionService` (new logic, writes to `documentos_padre` / `fragmentos_hijo`)

Each worker consumes from its own dedicated RabbitMQ queue, ensuring no message contention.

### Bulk Reprocessing Flow

```
Admin UI: /admin/reprocessing
  ├─ Pipeline selector: [Baseline Only] [Hierarchical Only] [Both Pipelines]
  ├─ Document selector: 
  │   ├─ [Select All] checkbox
  │   ├─ Filter by gazette (BOE / BOJA), date range
  │   └─ Multi-select table with document ID, title, date, processing status per pipeline
  └─ [Start Reprocessing] button

POST /api/admin/reprocess { pipeline_mode: "BOTH"|"BASELINE"|"HIERARCHICAL", document_ids: [...] | "ALL" }
  ├─ List matching S3 blobs (or use provided document_ids)
  ├─ If pipeline_mode == "BOTH" or "BASELINE":
  │   └─ Publish DocumentMessage to queue "documents_to_process_baseline" for each document
  ├─ If pipeline_mode == "BOTH" or "HIERARCHICAL":
  │   └─ Publish DocumentMessage to queue "documents_to_process_hierarchical" for each document
  └─ Return { enqueued_baseline: N, enqueued_hierarchical: M }
```

## Risks / Trade-offs

- **Risk:** LLM calls for Query Expansion and synthetic question generation add latency and cost.
  - *Mitigation:* Use a fast, cheap model (e.g., Llama 3 8B via Ollama or a lightweight API). Cache expansion results for repeated/similar queries. Track costs in `ingestion_metrics` to quantify the overhead for the TFG.
- **Risk:** The tsvector Spanish configuration may not cover all administrative vocabulary.
  - *Mitigation:* The hybrid approach means that even if sparse search misses a term, dense search can still find semantically similar fragments. We can add custom dictionaries later if needed.
- **Risk:** Parent documents may be very long, exceeding LLM context windows.
  - *Mitigation:* BOE articles and BOJA convocatoria sections are typically 1,000–3,000 tokens. If a parent exceeds 4,000 tokens, truncate to the relevant section surrounding the matched children.
- **Risk:** Low participation in the Question Arena may limit statistical significance.
  - *Mitigation:* Use a binomial test (`scipy`-equivalent in C#) which works with small samples. Target a minimum of 50 battles for a meaningful p-value.
- **Risk:** Running two Worker instances doubles resource consumption (CPU, RAM, API calls) during the 6-month backlog reprocessing.
  - *Mitigation:* Reprocessing is a one-time batch operation. Schedule it during off-peak hours. Workers can be scaled down to 0 replicas once reprocessing completes. The `ingestion_metrics` table tracks progress so reprocessing can be resumed if interrupted.
- **Risk:** RabbitMQ message ordering may differ between queues, leading to different processing order per pipeline.
  - *Mitigation:* Processing order does not affect the final dataset quality — every document is processed exactly once per pipeline. The `ingestion_metrics` table records timestamps for post-hoc ordering analysis if needed.
