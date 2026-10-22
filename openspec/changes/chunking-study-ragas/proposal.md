## Why

Chapter 7 of the thesis (comparative chunking study) and chapter 8 (tests and performance) need data the system cannot produce yet:

- Only one chunking strategy exists (`FixedOverlapChunkingStrategy`). The study compares four RAG variants: fixed size with overlap (the baseline pipeline), recursive splitting, semantic chunking and hierarchical parent-child (the hierarchical pipeline). Recursive and semantic are missing.
- There is no offline evaluation. The Arena measures human preference, but there is no fixed question set, no reference answers and no RAGAS scores, so the variants cannot be compared reproducibly.
- Latency is recorded per LLM call (`gen_ai.client.operation.duration`, `app.llm.time_to_first_token`, `AiCallLogs`), per Arena side (`ArenaBattle`) and per ingested document (`IngestionMetrics.ProcessingDurationMs`), but not per RAG stage (expansion, retrieval, generation) and there is no report with percentiles per variant.
- The Arena, the shadow test and the winner selection are hard-wired to two pipelines (`Baseline`, `Hierarchical`).

Decisions already taken: the final application answers with the variant that scores best in the evaluation, and the other variants can keep competing against it in the Arena as an A/B test with real users. All variants use one fixed embedding model (local Ollama `qwen3-embedding:0.6b`, multilingual, chosen over `nomic-embed-text` because the corpus and the questions are in Spanish) and the same generation model and questions, so only the chunking changes.

## What Changes

- Two new chunking strategies behind `IChunkingStrategy`: `RecursiveChunkingStrategy` (separators paragraph → line → sentence → word, same token budget and overlap as the fixed strategy) and `SemanticChunkingStrategy` (sentence embeddings with the platform embedding model, breakpoints where the distance between consecutive sentences is above a percentile threshold, bounded by minimum and maximum chunk size). `IChunkingStrategy` becomes asynchronous because the semantic strategy needs embeddings.
- Two new flat-chunk RAG variants, `Recursive` and `Semantic`, that reuse the baseline ingestion and answering code with their own strategy, worker mode, queue, Qdrant collection and job status. Chunks of every flat variant live in `DocumentChunks`, distinguished by a new `ChunkingStrategy` column.
- Query embeddings can carry an instruction prefix (`Ai:Embeddings:QueryInstruction`), as instruction-aware models such as Qwen3-Embedding expect; documents are embedded without it. Empty by default, so providers that do not use it (production `text-embedding-004`) are unaffected.
- Every `IRagPipeline` exposes an evaluation run that returns the answer, the retrieved contexts, the context sent to the model and per-stage timings, without streaming or persisting anything.
- A local evaluation harness: a .NET runner that asks the fixed question set to each variant over the same corpus and writes the raw results, and a Python RAGAS scorer that uses Ollama as judge. It produces per-question and per-variant tables (context precision, context recall, faithfulness, answer relevancy, hallucination rate, document hit rate and MRR), indexing cost and latency percentiles.
- An operational latency report (chat latency, time to first token, ingestion time per document, error rate) from the data the platform already stores, for chapter 8.
- The chat answers with a configured production variant (`Rag:ProductionPipeline`), and the Arena and the shadow test pair it with a challenger drawn from the other enabled variants.

## Capabilities

### New Capabilities
- `rag-evaluation`: fixed corpus and question set, evaluation runner, RAGAS scoring with a local judge, latency and indexing-cost reports.

### Modified Capabilities
- `chunking-embeddings`: asynchronous `IChunkingStrategy`, recursive and semantic strategies, flat-chunk variants sharing `DocumentChunks`.
- `bulk-reprocessing-pipeline`: `RECURSIVE` and `SEMANTIC` worker modes, their queues, job statuses and enqueue options.
- `qdrant-vector-store`: one collection per flat variant.
- `query-api-semantic-kernel`: streaming routed to the configured production variant; shadow test against a challenger.
- `question-arena`: battles between the production variant and a challenger from any enabled variant.

The change touches several capabilities because the study needs all four variants to be ingestible, answerable and comparable; splitting it would leave intermediate states where the Arena or the job state does not know a variant that the workers already write.

## Impact

- **Code**: `Application/Features/Ingestion` (chunking, `DocumentIngestionService`), `Application/Features/Chat` (pipelines, Arena, shadow test, winner selection), `Domain` (`PipelineType`, `DocumentJobState`, `DocumentChunk`), `Infrastructure` (consumers, Qdrant collections, EF migration), `Worker/Program.cs`, `AppHost`, `docker-compose.yml`, admin documents UI (pipeline options).
- **New projects/folders**: `src/AsistenteAyuntamiento.Evaluation` (.NET console runner) and `eval/` (dataset, Python RAGAS scorer, reports).
- **Database**: migration adding `DocumentChunks.ChunkingStrategy` and `DocumentJobStates.RecursiveStatus`/`SemanticStatus`; existing rows keep their data (`FixedOverlap`, `null`).
- **Messaging**: queues `documents_to_process_recursive` and `documents_to_process_semantic` with `.retry` and `.dlq` (`docs/messaging-contract.md`).
- **Cost**: the semantic strategy embeds every sentence at ingestion time; the study runs locally with Ollama, so it costs time, not money.

## Non-goals

- Changing the production embedding model or making it per variant: every variant uses the platform model, and the study runs locally with `qwen3-embedding:0.6b`.
- Making the hierarchical pipeline differ only in chunking: it keeps its enrichment, query expansion and hybrid search. The study reports this as a threat to validity instead of building a stripped-down hierarchical variant.
- Running RAGAS in CI or in production, or exposing evaluation through the public API.
- Switching the production variant automatically from RAGAS scores or Arena votes; the administrator sets `Rag:ProductionPipeline` after reading the results.
- Human-written reference answers for every question beyond the set used in the study, and a user-facing evaluation UI.
- Writing chapters 7 and 8 of the thesis (`memoria/`).
