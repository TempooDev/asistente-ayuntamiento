## 1. Data

- [x] 1.1 Add `AiCallLog.TimeToFirstTokenMs` and `AiCallLog.Provider` and fill them from `AiCallOutcome` in `AiCallRecorder` (TTFT covers task 4.3 of `chunking-study-ragas`).
- [x] 1.2 Add `EvaluationRun` and `EvaluationPipelineResult` entities (schema `evaluation`, no tenant) and their EF configuration.
- [x] 1.3 One migration for 1.1 and 1.2; Testcontainers test that existing `AiCallLogs` rows keep a null TTFT.

## 2. Metrics API

- [x] 2.1 `from`/`to` on `GET /api/ai/metrics/summary` and `/ingestion` with validation (400 on inverted or > 366-day ranges, default last 30 days).
- [x] 2.2 Percentiles with `percentile_cont` interpolation over the period's durations (answer latency, TTFT, ingestion duration), see design decision 2.
- [x] 2.3 Summary: percentiles, TTFT p50, tokens per answer, estimated cost (total, per step, per pipeline, per 1,000 answers), unpriced models, daily series, per-pipeline rows; exclude `Pipeline = evaluation`.
- [x] 2.4 Ingestion: latest row per document and pipeline, per-pipeline figures and failed documents from `DocumentJobStates`.
- [x] 2.5 Tests in `Application.Tests` and `Infrastructure.Tests` (percentiles against Postgres), and update the DTOs in `docs/`.

## 3. Evaluation results

- [x] 3.1 `--import {runId}` in `AsistenteAyuntamiento.Evaluation`: read the run files, refuse unscored runs, upsert by run id; tests with the scorer's fixture run.
- [x] 3.2 `IEvaluationResultsService` and `GET /api/evaluation/runs`, `GET /api/evaluation/runs/{runId}` under `PlatformAdmin`, with authorization tests.

## 4. Documents page

- [x] 4.1 `ListBlobsAsync`: per-pipeline statuses and last error per item, `pipelineStats`, `pipeline` filter, 10-second cache (`IngestionAdminService.cs`, endpoint and DTOs), with tests.
- [x] 4.2 Documents page: one progress bar per pipeline, one status column per pipeline with the error tooltip, "Todos" as default filter, refresh every 10 seconds while something is queued or processing (`features/documents/`), with component tests.

## 5. Metrics frontend

- [x] 5.1 Add `chart.js` and `ng2-charts`, loaded only by the analytics route; chart colours from CSS variables, one colour per pipeline.
- [x] 5.2 Period selector with previous-period deltas.
- [x] 5.3 Usage section: cards, daily chart (answers + p95 line), per-pipeline table, cost by step, unpriced notice.
- [x] 5.4 Ingestion section per pipeline.
- [x] 5.5 Offline evaluation section: run selector, grouped bar charts, history chart, empty state.
- [x] 5.6 Arena at the end, collapsed and requested on expand.
- [x] 5.7 Component tests for role visibility, empty states and "—" for missing TTFT.

## 6. Docs and specs

- [x] 6.1 Document `--import` in `eval/README.md` and the new endpoints.
- [x] 6.2 `openspec validate metrics-dashboard-redesign --strict`.
