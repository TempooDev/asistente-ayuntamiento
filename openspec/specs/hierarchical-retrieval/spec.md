# Specification: Hierarchical Parent-Child Retrieval with Hybrid Search

## Purpose
Replace the flat chunking retrieval strategy with a hierarchical Parent-Child document model, hybrid search (dense + sparse RRF), LLM-powered query expansion, and clear-language generation optimized for citizen comprehension.

## Requirements

### Requirement: Parent-Child Document Schema
The system SHALL store legal documents in a two-level hierarchy where parent records contain the full article text and child records contain fine-grained searchable fragments.

#### Scenario: Ingesting a BOE article
- **WHEN** the BOE ingestion service processes a consolidated law XML
- **THEN** it SHALL create one `DocumentoPadre` record per `<articulo>` or disposition block
- **AND** create one or more `FragmentoHijo` records (250–400 tokens each) for each numbered sub-section or paragraph within the article.

#### Scenario: Ingesting a BOJA convocatoria
- **WHEN** the BOJA ingestion service processes a JSON feed entry
- **THEN** it SHALL create one `DocumentoPadre` record per article or requirement section
- **AND** create subordinate `FragmentoHijo` records decomposed by clause or paragraph.

### Requirement: Child Fragment Contextual Enrichment
The system SHALL enrich each child fragment with a contextual breadcrumb and synthetic citizen questions before embedding.

#### Scenario: Generating enriched fragment text
- **WHEN** a child fragment is created during ingestion
- **THEN** its `ChunkText` SHALL contain: (1) a breadcrumb line with gazette, issuing body, regulation name, article, and sub-section, (2) two LLM-generated citizen questions that the fragment would answer, and (3) the body text of the legal clause.

### Requirement: Query Expansion
The system SHALL translate citizen plain-language queries into administrative terminology before retrieval.

#### Scenario: Expanding a colloquial query
- **WHEN** a user submits a query like "where do I apply for rent help if I'm 24"
- **THEN** the system SHALL call the LLM to produce a `query_lexica` (tsquery-compatible administrative terms), a `query_semantica` (formal expanded phrase for embedding), and a `filtro_municipio` (detected municipality or null).

### Requirement: Hybrid Search with Reciprocal Rank Fusion
The system SHALL combine dense vector similarity and sparse full-text search rankings to retrieve the most relevant child fragments.

#### Scenario: Executing a hybrid search
- **WHEN** an expanded query is submitted to the retrieval service
- **THEN** the system SHALL execute a dense ranking (HNSW cosine distance) and a sparse ranking (GIN tsvector `ts_rank`) in a single SQL query
- **AND** fuse both rankings using RRF (`1/(k + rank)` with k=60) to produce a final ranked list of the top 5 child fragments.

#### Scenario: Filtering by municipality
- **WHEN** the query expansion detects a municipality filter
- **THEN** the hybrid search SHALL restrict results to fragments matching that municipality or fragments with no municipality (state/regional scope).

### Requirement: Parent Resolution for Generation
The system SHALL resolve matched child fragments to their parent documents and provide the full parent text to the LLM.

#### Scenario: Resolving parent context
- **WHEN** the top 5 child fragments are retrieved
- **THEN** the system SHALL fetch the distinct `ParentDocuments.FullText` records referenced by those children
- **AND** pass them as context to the generation service.

### Requirement: Clear-Language Generation
The system SHALL generate citizen-friendly answers that explain legal regulations without jargon, organized with practical headings and source citations.

#### Scenario: Generating a structured response
- **WHEN** parent texts are provided to the generation service
- **THEN** the LLM SHALL produce a response organized with "Requirements", "Key Deadlines", "Documents to Provide", and "Where to Complete the Procedure" headings
- **AND** include a final "Official Sources Consulted" section citing the exact gazette, regulation, and article.

#### Scenario: Insufficient context
- **WHEN** the retrieved fragments do not contain enough information for a confident answer
- **THEN** the system SHALL transparently indicate this and suggest visiting the citizen services office.

## Architecture Decision Records (ADR)

### ADR: Flat Large-Chunk Retrieval vs. Hierarchical Parent-Child Retrieval

**Decision:** We adopt a hierarchical Parent-Child Retrieval strategy to replace the current flat 6,000-token chunking approach.

**Context and Problem Statement:**
The existing system embeds documents as flat blocks of up to ~6,000 tokens (leveraging Qwen3-Embedding-8B's large context window). While this preserves broad context within each chunk, it creates several problems for a citizen-facing legal assistant:

1. **Low retrieval precision:** A 6,000-token chunk may contain multiple unrelated articles or sections. When a citizen asks about a specific requirement (e.g., age limits for a housing subsidy), the retrieved chunk includes surrounding irrelevant text that wastes LLM context and can confuse the generation.
2. **No lexical recall:** Pure dense vector search struggles with exact terms critical in legal retrieval — article numbers, law identifiers (e.g., "BOE-A-2024-1234"), specific dates, and proper nouns. There is no full-text search complement.
3. **No query adaptation:** Citizens use colloquial language while legal texts use administrative jargon. Without query expansion, the semantic gap between "help for rent" and "subvención de vivienda" reduces recall.
4. **Opaque generation:** The LLM reproduces legal phrasing verbatim because it receives raw legal text without guidance to simplify it for citizens.

**Considered Options:**

| Criterion | Option A: Flat Large Chunks (Baseline) | Option B: Parent-Child + Hybrid Search (Chosen) |
|---|---|---|
| **Chunk size** | ~6,000 tokens (single level) | Parent: full article (1,000–3,000 tokens); Child: 250–400 tokens (two levels) |
| **Search type** | Dense vector only (HNSW) | Hybrid: Dense (HNSW) + Sparse (GIN tsvector) fused via RRF |
| **Query processing** | Direct embedding of raw user query | LLM Query Expansion: lexical terms + semantic phrase + municipality filter |
| **Context for LLM** | Raw chunk text (may span multiple articles) | Full parent article text (single coherent legal unit) |
| **Embedding cost** | Lower total embeddings (fewer, larger chunks) | Higher total embeddings (more, smaller chunks) + LLM enrichment calls |
| **Retrieval precision** | Lower (noisy context) | Higher (fine-grained child matching) |
| **Retrieval recall** | Lower (no lexical signal) | Higher (hybrid search captures both semantic and lexical matches) |
| **Generation quality** | Jargon-heavy, unstructured | Clear language, structured headings, source citations |
| **Latency** | ~50ms retrieval | ~100ms retrieval (hybrid) + ~200ms query expansion |
| **Incremental cost** | None (current system) | LLM calls for query expansion (~1/query) and enrichment (~2 questions/chunk, one-time at ingestion) |

**Decision Rationale:**
- **Precision over cost:** The marginal increase in embedding tokens and LLM enrichment calls is negligible compared to the quality improvement. At ~$0.01/1M tokens, even doubling the chunk count adds cents, not dollars.
- **Hybrid search is strictly better:** Adding sparse search can only improve recall — it never harms dense search results since RRF combines both signals without requiring learned weights.
- **Parent resolution preserves coherence:** Unlike flat chunks that may cut across articles, parent resolution guarantees the LLM receives a complete, coherent legal unit as context.
- **Measurable improvement:** The Question Arena will provide empirical human evaluation data, and the `IngestionMetrics` table will quantify the exact cost trade-off for the TFG thesis.

**Consequences:**
- The `chunks_baseline_v1` table is preserved unchanged for the ablation study.
- Both pipelines run concurrently in the Question Arena, allowing direct comparison under identical conditions.
- The `IngestionMetrics` table records the cost differential (tokens embedded, LLM calls, processing time) between both strategies.
- Future phases can add re-ranking or cross-encoder stages on top of the hybrid results without architectural changes.
