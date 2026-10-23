## Context

Today there are two RAG variants. The baseline indexes flat chunks from `FixedOverlapChunkingStrategy` (`DocumentChunks` + Qdrant `document_chunks`) and answers with a dense search and the tenant's chat model. The hierarchical one stores parents and enriched child fragments (`ParentDocuments`, `ChildFragments`, Qdrant `child_fragments`), expands the query and fuses dense and full-text rankings with RRF. Both are `IRagPipeline` implementations selected by `PipelineType` (`Baseline = 1`, `Hierarchical = 2`), and each has its own worker mode, queue and job status.

The two-pipeline assumption is spread over `PipelineType`, `IngestionPipelines` (flags `Baseline | Hierarchical`), `DocumentJobState` (two status columns), `Worker/Program.cs`, `ArenaService`, `ArenaStreamingService` (50/50 between the two), `ShadowTestScheduler` ("the other pipeline") and `WinningPipelineSelector` (more wins of two).

## Goals / Non-Goals

**Goals:**
- Four variants ingest the same corpus and answer the same questions with the same embedding and generation models.
- A reproducible local evaluation (RAGAS with Ollama as judge) plus deterministic retrieval metrics, latency per stage and indexing cost.
- The final app answers with the chosen variant; the others stay available as Arena challengers.

**Non-Goals:** see the proposal.

## Decisions

### 1. Recursive and semantic are flat-chunk variants of the baseline
They reuse `DocumentIngestionService` and `BaselineRagPipeline` (renamed `FlatChunkRagPipeline`), parameterized by a `FlatChunkVariant` record: `PipelineType`, keyed `IChunkingStrategy`, `VectorCollection` and the `DocumentChunks.ChunkingStrategy` value. The baseline remains the `Baseline` variant with `FixedOverlap`, so existing data, URLs and specs keep their meaning.

*Why:* the only thing that should change between fixed, recursive and semantic is the chunking; sharing ingestion and answering code guarantees it. *Alternative:* new entities and tables per strategy (what chapter 5 of the thesis anticipated). Rejected: three identical tables and services for no difference in shape.

`DocumentChunks` gets a `ChunkingStrategy` column (`FixedOverlap` for existing rows, index on `(DocumentId, ChunkingStrategy)`); re-ingesting a document deletes only the rows of its own strategy. The hierarchical pipeline keeps using the fixed strategy to split long articles (unkeyed default registration).

### 2. Asynchronous chunking
`IChunkingStrategy.ChunkTextAsync(string text, CancellationToken)` returns `Task<IReadOnlyList<DocumentChunkResult>>`. The fixed and recursive strategies complete synchronously; the semantic one awaits sentence embeddings. Callers (`DocumentIngestionService`, `BoeIngestionService`, `BojaIngestionService`) await it.

### 3. Strategy parameters
All three flat strategies use the same token counter as `TextChunker` (its default estimate), the same `ChunkMaxTokens` (400) and, where it applies, the same `ChunkOverlapTokens` (50).

- **Recursive** (`Ai:Chunking:Recursive:*`): separators `\n\n`, `\n`, sentence end (`. `, `; `, `: `), space, then characters; a piece that fits is kept whole, a piece that does not is split with the next separator; adjacent pieces are merged up to the limit and consecutive chunks share up to `ChunkOverlapTokens`.
- **Semantic** (`Ai:Chunking:Semantic:*`): sentences split by a Spanish-aware splitter that does not cut abbreviations common in bulletins (`art.`, `núm.`, `apdo.`, `Sr.`, `D.`, `Dña.`, ordinal numbers, `1.º`); each sentence is embedded together with its neighbours (`BufferSize`, default 1); a breakpoint is placed where the cosine distance between consecutive windows is above the `BreakpointPercentile` (default 95) of the document's distances; chunks under `MinTokens` (default 100) are merged with the next, and chunks over `ChunkMaxTokens` are split with the recursive strategy. Embeddings go in batches of 100 with the platform generator, and their tokens are added to the ingestion metrics of the document.

### 4. New pipeline types, queues and worker modes
`PipelineType.Recursive = 3` and `PipelineType.Semantic = 4`; `IngestionPipelines.Recursive = 4`, `Semantic = 8`, `All`; `DocumentJobState.RecursiveStatus` and `SemanticStatus`, included in `Summarize`. Worker modes `RECURSIVE` and `SEMANTIC` consume `documents_to_process_recursive` and `documents_to_process_semantic` with the existing retry and dead-letter policy. Aspire always runs four workers; `docker-compose.yml` adds `worker-recursive` and `worker-semantic` under the profile `chunking-study`, so production runs them only when enabled.

### 5. Production variant and Arena challengers
`Rag:ProductionPipeline` names the variant that answers the chat. When it is not set, the current behaviour stays (the variant with more Arena wins, `Baseline` on ties), now over all enabled variants. `Rag:ArenaPipelines` lists the enabled variants (default `Baseline,Hierarchical`, so nothing changes until the study variants are ingested). Each Arena battle and each shadow test pairs the production variant with a challenger chosen uniformly from the other enabled variants; Alfa/Beta stay random 50/50.

### 6. Evaluation run on the pipeline
`IRagPipeline.RunForEvaluationAsync(EvaluationTurn, CancellationToken)` returns `RagEvaluationResult`: answer, retrieved contexts (ordered, with document id, score and text), the context text given to the model, token usage and timings (`ExpansionMs`, `RetrievalMs`, `TimeToFirstTokenMs`, `GenerationMs`, `TotalMs`). It does not persist messages or battles and is only resolved by the evaluation runner. For the hierarchical variant the retrieved contexts are the fused child fragments (what the search ranked) and the model context is the parents' full text, so context precision/recall measure retrieval and faithfulness is measured against what the model actually read.

### 7. Runner in .NET, scoring in Python
`src/AsistenteAyuntamiento.Evaluation` is a console host that builds the same DI graph as the API (`AddApplicationServices` + `AddInfrastructureServices`) and calls each pipeline in-process, so no HTTP endpoint, token or tenant setup is needed. It runs as an Aspire resource with explicit start. It writes `eval/runs/{runId}/answers.jsonl` and `run.json` (models, settings, git commit, corpus hash). RAGAS is Python only, so `eval/ragas/` scores those files with `ragas` and `langchain-ollama` in a virtual environment pinned to Python 3.12 (the machine has 3.15 rc, which RAGAS does not support yet).

*Alternative:* an admin endpoint called by the Python script. Rejected: it adds a security surface and auth plumbing to an offline tool.

### 8. Same models, controlled order
The runner overrides the chat and ingestion models with `Evaluation:ChatModel` (default Ollama `qwen2.5:7b-instruct-q4_K_M`, temperature 0) for every variant and refuses to start if any study collection has a vector size other than the configured embedding model's. Per question, the order of the variants is shuffled with a seed stored in `run.json`; the first question of each variant is a warm-up excluded from latency statistics.

### 9. Embedding model and query instruction
The study embeds with Ollama `qwen3-embedding:0.6b` (1024 dimensions, 32k context), replacing `nomic-embed-text` as the local default: it is multilingual and the corpus and questions are Spanish, while nomic was trained mostly on English. The 0.6B size keeps the semantic strategy, which embeds one window per sentence, affordable on a laptop; the 4B and 8B sizes are not worth their cost here. Collections are created with the size of the first vector, so the existing 768-dimension local collections are deleted and the corpus is re-ingested.

Qwen3-Embedding is instruction-aware: queries perform better as `Instruct: {task}\nQuery: {text}`, while documents go without a prefix. The task text lives with the other prompts in `PromptCatalog.EmbeddingQueryInstruction` (in English, as the Qwen authors advise for multilingual use: `Given a question from a citizen or municipal staff, retrieve passages of Spanish official bulletins (BOE and BOJA) that answer it`) and `Ai:Embeddings:UseQueryInstruction` turns it on (the local AppHost does); when it is on, every query embedding (baseline search, hybrid retrieval, Arena) is prefixed, and ingestion and the semantic strategy's sentence windows are not. When it is off nothing changes, which keeps production (`text-embedding-004`) as it is.

*Alternative:* keep `nomic-embed-text` with its own `search_query:`/`search_document:` prefixes. Rejected: weaker in Spanish, and the prefixes are not used today either.

### 10. Metrics
- RAGAS: `LLMContextPrecisionWithReference`, `LLMContextRecall`, `Faithfulness`, `ResponseRelevancy` (embeddings `qwen3-embedding:0.6b`).
- Hallucination rate: share of answers with faithfulness below `HallucinationThreshold` (default 0.8).
- Deterministic retrieval: document hit rate@k and MRR against `reference_document_ids`, which do not depend on the judge.
- Latency: p50, p95 and mean per stage and variant.
- Indexing: per variant from `IngestionMetrics` (ms per document, chunks per document, embedded tokens, LLM calls and tokens) and points per Qdrant collection.

## Risks / Trade-offs

- **Local judge quality** → small models score noisily. Mitigation: deterministic hit rate/MRR next to RAGAS, a configurable judge (`EVAL_JUDGE_MODEL`, a larger model if the machine allows) and per-question tables so outliers can be inspected.
- **Hierarchical differs in more than chunking** → reported as a threat to validity; its retrieval contexts are scored like the others.
- **Semantic ingestion time** → one embedding per sentence window; the corpus is a fixed subset, and the time is reported as indexing cost.
- **Arena data split** → battles are now spread over several challengers; the dashboard keeps per-variant aggregates and the production decision does not depend on votes.

## Migration Plan

1. Deploy the migration (new nullable status columns, `ChunkingStrategy` defaulting to `FixedOverlap`).
2. With `Rag:ArenaPipelines` unset, behaviour is unchanged.
3. For the study: run the four workers locally, enqueue the corpus with `pipelineMode = All`, run the runner and the scorer.
4. After the study, set `Rag:ProductionPipeline` and, if wanted, add the study variants to `Rag:ArenaPipelines` in production after ingesting them there.

Rollback: unset both settings; extra columns and collections are ignored.

## Open Questions

- Size of the question set (proposed 40, at least 30) and the corpus subset (proposed ~300 documents of BOE and BOJA covering the questions).
- Judge model: the same 7B as the generator or a larger one if hardware allows.
