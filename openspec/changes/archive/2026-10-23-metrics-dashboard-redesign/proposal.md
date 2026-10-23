## Why

The "Métricas" page (`admin/analytics`) shows all-time totals that say little: there is no period, latency is a single average (no percentiles, no time to first token), tokens are raw numbers without cost or per-answer figures, nothing is split by RAG pipeline, and "Documentos ingeridos" counts `IngestionMetrics` rows, so a re-ingested document counts twice. The Arena takes most of the page although it has almost no votes yet. The offline evaluation (`chunking-study-ragas`) produces RAGAS scores, retrieval metrics and latency percentiles that only live in CSV files, so they cannot be compared across runs or shown next to the operational data.

## What Changes

- One period selector (last 7 days, last 30 days or custom dates) for the whole page, with the change against the previous period of the same length.
- Organization usage: answers, success rate, latency p50/p95 and time to first token p50 as cards; a daily chart of answers with a p95 latency line; a table per pipeline (answers, success rate, p50/p95, tokens per answer, estimated cost per 1,000 answers); estimated cost split by step (answer, query expansion, history analysis). Calls of the evaluation runner (`app.pipeline = evaluation`) are excluded.
- Platform ingestion per pipeline, counting each document once (its latest `IngestionMetrics` row): documents, seconds per document p50/p95, chunks per document, embedded tokens, enrichment calls and tokens, and failed documents from `DocumentJobStates`.
- Offline evaluation results stored in the database: the runner imports a scored run (`run.json`, `summary.csv`, `latency.csv`, `indexing.csv`, `scoring.json`) into `EvaluationRuns` and `EvaluationPipelineResults`, and the page shows the selected run as grouped bar charts per pipeline (RAGAS metrics, hit@k and MRR, latency) and the history of runs.
- The Arena section moves to the end of the page, collapsed by default.
- The documents page shows the progress of each pipeline (one bar per pipeline and one status column per pipeline, refreshed every 10 seconds while something is queued or processing). Today it only shows the summary status, so a document completed by the baseline and queued for the hierarchical pipeline counts as "queued" and the progress of each pipeline is invisible.
- Charts with Chart.js through `ng2-charts`, themed for light and dark mode.

## Capabilities

### New Capabilities
- `evaluation-results`: storage of scored evaluation runs and their read API.

### Modified Capabilities
- `admin-metrics-dashboard`: period filter, percentiles, daily series, per-pipeline usage and cost, per-pipeline ingestion, evaluation results section, collapsed Arena.
- `ingestion-administration`: per-pipeline statuses, counters and progress on the documents page.

## Impact

- **API**: `GET /api/ai/metrics/summary` and `GET /api/ai/metrics/ingestion` gain `from`/`to` and return percentiles, daily series and per-pipeline rows; new `GET /api/evaluation/runs` and `GET /api/evaluation/runs/{runId}` (policy `PlatformAdmin`).
- **Database**: `AiCallLogs.TimeToFirstTokenMs` (task 4.3 of `chunking-study-ragas`, implemented here) and `AiCallLogs.Provider`, new schema `evaluation` with `EvaluationRuns` and `EvaluationPipelineResults`; one migration.
- **Runner**: `AsistenteAyuntamiento.Evaluation --import {runId}`.
- **Frontend**: `features/documents` with per-pipeline bars, columns and auto-refresh; `features/analytics` rewritten into sections; new dependency `chart.js` + `ng2-charts`.

## Non-goals

- Per-question evaluation scores in the database (they stay in `scores.csv`).
- Real-time dashboards or Grafana panels (observability already covers live metrics).
- New Arena features; its data and API stay as they are.
- Exporting the charts for the thesis (the CSVs of the evaluation remain the source).
