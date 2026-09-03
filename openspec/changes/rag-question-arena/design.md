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

## Risks / Trade-offs

- **Risk:** LLM calls for Query Expansion and synthetic question generation add latency and cost.
  - *Mitigation:* Use a fast, cheap model (e.g., Llama 3 8B via Ollama or a lightweight API). Cache expansion results for repeated/similar queries. Track costs in `ingestion_metrics` to quantify the overhead for the TFG.
- **Risk:** The tsvector Spanish configuration may not cover all administrative vocabulary.
  - *Mitigation:* The hybrid approach means that even if sparse search misses a term, dense search can still find semantically similar fragments. We can add custom dictionaries later if needed.
- **Risk:** Parent documents may be very long, exceeding LLM context windows.
  - *Mitigation:* BOE articles and BOJA convocatoria sections are typically 1,000–3,000 tokens. If a parent exceeds 4,000 tokens, truncate to the relevant section surrounding the matched children.
- **Risk:** Low participation in the Question Arena may limit statistical significance.
  - *Mitigation:* Use a binomial test (`scipy`-equivalent in C#) which works with small samples. Target a minimum of 50 battles for a meaningful p-value.
