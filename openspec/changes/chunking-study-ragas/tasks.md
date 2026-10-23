## 1. Asynchronous chunking contract

- [ ] 1.1 Change `IChunkingStrategy` to `ChunkTextAsync(string, CancellationToken)` returning `IReadOnlyList<DocumentChunkResult>`, add an optional `EmbeddedTokens` to the result, and adapt `FixedOverlapChunkingStrategy` (`src/AsistenteAyuntamiento.Application/Features/Ingestion/Chunking/IChunkingStrategy.cs`, `FixedOverlapChunkingStrategy.cs`, `DocumentChunkResult.cs`).
- [ ] 1.2 Await the new method in `DocumentIngestionService.cs`, `BoeIngestionService.cs` and `BojaIngestionService.cs`, and update `tests/AsistenteAyuntamiento.Application.Tests/Features/Ingestion/Chunking/FixedOverlapChunkingStrategyTests.cs` and the ingestion tests that mock the strategy.
- [ ] 1.3 Extract the token estimator used by `TextChunker` into a shared `ChunkTokenCounter` used by every strategy (`Application/Features/Ingestion/Chunking/ChunkTokenCounter.cs`).
- [x] 1.4 Add `PromptCatalog.EmbeddingQueryInstruction`, the switch `Ai:Embeddings:UseQueryInstruction` and a small query-embedding helper that applies `Instruct: …\nQuery: …` when it is on; use it in `BaselineRagPipeline.cs`, `HybridRetrievalService.cs` and `ArenaService.cs`, turn it on in the local AppHost and document it in `docs/setup.md`, with tests for both cases.

## 2. Recursive strategy

- [ ] 2.1 Implement `RecursiveChunkingStrategy` (separators, merge, overlap by whole pieces, character fallback) with settings `Ai:Chunking:Recursive:*` falling back to `Ai:Embeddings:*` (`Application/Features/Ingestion/Chunking/RecursiveChunkingStrategy.cs`).
- [ ] 2.2 Unit tests for paragraphs that fit, long paragraphs, text without separators, overlap and limits (`tests/AsistenteAyuntamiento.Application.Tests/Features/Ingestion/Chunking/RecursiveChunkingStrategyTests.cs`).

## 3. Semantic strategy

- [ ] 3.1 Implement `SpanishSentenceSplitter` with the bulletin abbreviation list and tests (`Application/Features/Ingestion/Chunking/SpanishSentenceSplitter.cs`, `tests/.../Chunking/SpanishSentenceSplitterTests.cs`).
- [ ] 3.2 Implement `SemanticChunkingStrategy` (buffered sentence windows, batched embeddings, percentile breakpoints, min merge, recursive split over the maximum, embedded tokens) with settings `Ai:Chunking:Semantic:*` (`Application/Features/Ingestion/Chunking/SemanticChunkingStrategy.cs`).
- [ ] 3.3 Unit tests with a fake embedding generator: topic change, short text, empty text without calls, oversize chunk, embedding failure (`tests/.../Chunking/SemanticChunkingStrategyTests.cs`).
- [ ] 3.4 Register the three strategies as keyed singletons (`FixedOverlap` also unkeyed) with startup validation (`src/AsistenteAyuntamiento.Application/DependencyInjection.cs`), and add the settings to `appsettings.json`, `docker-compose.yml` and `docs/setup.md`.

## 4. Domain, persistence and vectors

- [ ] 4.1 Add `PipelineType.Recursive = 3` and `Semantic = 4`, `IngestionPipelines.Recursive`, `Semantic` and `All`, and `ChunkingStrategy` on `DocumentChunk` (`src/AsistenteAyuntamiento.Domain/Common/Enums/PipelineType.cs`, `Application/Common/Messaging/IngestionPipelines.cs`, `Domain/Features/Ingestion/DocumentChunk.cs`).
- [ ] 4.2 Add `RecursiveStatus` and `SemanticStatus` to `DocumentJobState`, handle them in `SetPipelineStatus`, `Reset` and `Summarize`, and extend `DocumentJobStateUpdates.cs` and `IngestionAdminService.cs` (`Domain/Features/Ingestion/DocumentJobState.cs`); update `tests/AsistenteAyuntamiento.Domain.Tests`.
- [x] 4.3 Add `TimeToFirstTokenMs` to `AiCallLog` and fill it where streamed calls record time to first token (`Domain/Features/Chat/AiCallLog.cs`, `Application/Features/Chat/AiCallRecorder.cs`, `Application/Features/Chat/Telemetry/MeteredChatCompletion.cs`).
- [ ] 4.4 EF configuration and one migration: `DocumentChunks.ChunkingStrategy` (default `FixedOverlap`, index with `DocumentId`), the two status columns and `AiCallLogs.TimeToFirstTokenMs` (`src/AsistenteAyuntamiento.Infrastructure/Data/`), with a Testcontainers test that existing rows keep `FixedOverlap`.
- [ ] 4.5 Add `VectorCollection.RecursiveChunks` and `SemanticChunks` with settings `VectorStore:RecursiveChunksCollection` and `VectorStore:SemanticChunksCollection` (`Application/Common/VectorStore/VectorCollection.cs`, `Infrastructure/VectorStore/QdrantVectorStore.cs`), including bootstrap and delete-by-document tests.

## 5. Flat-chunk variants (ingestion)

- [ ] 5.1 Introduce `FlatChunkVariant` and parameterize `DocumentIngestionService` by it: keyed strategy, `ChunkingStrategy` filter on delete/insert, collection, `PipelineType` for job state and metrics, embedded chunking tokens in the metrics (`Application/Features/Ingestion/DocumentIngestionService.cs`, `FlatChunkVariant.cs`).
- [ ] 5.2 Add queues `documents_to_process_recursive` and `documents_to_process_semantic` (with `.retry`, `.dlq`) and a generic flat-chunk consumer per variant; update `docs/messaging-contract.md` (`src/AsistenteAyuntamiento.Infrastructure/Features/Ingestion/`, `Infrastructure/Messaging/RabbitMqTopology.cs`).
- [ ] 5.3 Worker modes `RECURSIVE` and `SEMANTIC` (`src/AsistenteAyuntamiento.Worker/Program.cs`) and tests in `tests/AsistenteAyuntamiento.Worker.Tests`.
- [ ] 5.4 Add `worker-recursive` and `worker-semantic` to the AppHost (`src/AsistenteAyuntamiento.AppHost/AppHost.cs`) and to `docker-compose.yml` under the profile `chunking-study`; document it in `docs/deploy-dokploy.md`.
- [ ] 5.5 Accept `RECURSIVE`, `SEMANTIC`, combinations and `ALL` in `pipelineMode` (`ApiService/Features/Ingestion/IngestionEndpoints.cs`, `IngestionAdminService.cs`) with the 403 test for non platform admins, and add the options to the admin documents page (`src/AsistenteAyuntamiento.Angular/src/app/features/documents/`).

## 6. Flat-chunk variants (answering) and evaluation run

- [ ] 6.1 Rename `BaselineRagPipeline` to `FlatChunkRagPipeline` parameterized by `FlatChunkVariant`, register one `IRagPipeline` per variant (`Application/Features/Chat/Pipelines/BaselineRagPipeline.cs`, `DependencyInjection.cs`, `AiChatService.cs`), keeping the baseline's behaviour and tests.
- [x] 6.2 Split retrieval and generation inside both pipelines and add `RunForEvaluationAsync` with `RagEvaluationResult` and stage timings (`Application/Features/Chat/Pipelines/IRagPipeline.cs`, `FlatChunkRagPipeline.cs`, `HierarchicalRagPipeline.cs`, `RagEvaluationResult.cs`).
- [x] 6.3 Tests: evaluation run returns contexts and timings, persists nothing, maps model errors to codes (`tests/AsistenteAyuntamiento.Application.Tests/Features/Chat/Pipelines/`).

## 7. Production variant and Arena

- [ ] 7.1 Add `Rag:ProductionPipeline` and `Rag:ArenaPipelines` options with startup validation, and make `WinningPipelineSelector` return the configured variant or the most-winning enabled one (`Application/Features/Chat/WinningPipelineSelector.cs`, `RagOptions.cs`).
- [ ] 7.2 Pick a challenger among the other enabled pipelines in `ArenaStreamingService.cs`, `ArenaService.cs` and `ShadowTestScheduler.cs`; tests for default pairing, challenger distribution and single-pipeline (no shadow).
- [ ] 7.3 Per-pipeline and per-pair aggregates in `ArenaAnalyticsService.cs` and `PipelineMetrics`, and labels for the new variants in the Arena reveal and the metrics page (`src/AsistenteAyuntamiento.Angular/src/app/features/arena/`, `features/analytics/`).

## 8. Evaluation dataset and runner

- [x] 8.1 Create `eval/README.md` and `eval/dataset/` with `corpus.txt` (study subset of BOE and BOJA) and a `questions.jsonl` template with five example questions.
- [ ] 8.2 Write the full question set (≥ 30, proposed 40, including `sin-respuesta`) with references and document ids (manual task for Antonio; Claude can draft candidates from the corpus for review).
- [x] 8.3 Create `src/AsistenteAyuntamiento.Evaluation` (console host, `AddApplicationServices` + `AddInfrastructureServices`, model override, pre-flight checks, shuffled order, `answers.jsonl`, `run.json`, `--resume`, `--pipelines`) and add it to the solution and, with explicit start, to the AppHost.
- [x] 8.4 Tests for the pre-flight checks and the output format (`tests/AsistenteAyuntamiento.Evaluation.Tests` or in `Application.Tests`), and allow the new project in `tests/AsistenteAyuntamiento.ArchitectureTests`.

## 9. RAGAS scoring and reports

- [x] 9.1 Create `eval/ragas/` (Python 3.12 project with lock file, `validate` command for the dataset) (`eval/ragas/pyproject.toml`, `eval/ragas/validate_dataset.py`).
- [x] 9.2 Implement scoring with RAGAS and the Ollama judge plus hit@k and MRR, writing `scores.csv`, `summary.csv` and `scoring.json` (`eval/ragas/score.py`).
- [x] 9.3 Implement `latency.csv` and `indexing.csv` from the run and from `IngestionMetrics` (`eval/ragas/report.py`).
- [ ] 9.4 Implement the operational latency report over a date range with a read-only connection (`eval/latency/report.py` or `.sql`).
- [x] 9.5 Small fixture run (3 questions, 4 pipelines) checked into `eval/ragas/tests/` to test the scorer offline with a stubbed judge.

## 10. Study execution

- [ ] 10.1 Locally with Ollama (`qwen3-embedding:0.6b`, `qwen2.5:7b-instruct-q4_K_M`): delete the 768-dimension collections, recreate all of them at 1024 dimensions, enqueue the corpus with `pipelineMode=ALL` and wait for the four statuses to be `Completed`.
- [ ] 10.2 Run the runner and the scorer; keep `eval/runs/{runId}/` with `run.json`, `scores.csv`, `summary.csv`, `latency.csv` and `indexing.csv` for chapter 7 and appendix A.
- [ ] 10.3 Run the operational latency report for chapter 8.
- [ ] 10.4 Set `Rag:ProductionPipeline` to the winning variant and decide which variants stay in `Rag:ArenaPipelines`.

## 11. Specs and docs

- [ ] 11.1 Update `openspec/config.yaml` context (four variants, worker modes, production variant) and `docs/architecture.md`.
- [ ] 11.2 Run `openspec validate chunking-study-ragas --strict` and fix findings before archiving.
