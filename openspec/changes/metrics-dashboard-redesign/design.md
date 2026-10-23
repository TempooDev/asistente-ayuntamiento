## Context

`AiMetricsQueryService` aggregates `AiCallLogs` of the tenant (query filter) into all-time totals and `IngestionMetrics` into platform totals. `AiCallLogs` stores duration, tokens, pipeline and operation per call, but not time to first token. Prices per model live in `Ai:Pricing:{provider}:{model}` and `LlmUsageMetrics.EstimateCost` already computes cost from them. The evaluation runner writes `eval/runs/{runId}/` and the Python scorer adds `scores.csv`, `summary.csv`, `latency.csv`, `indexing.csv` and `scoring.json`.

## Goals / Non-Goals

**Goals:** a page that answers "how fast, how reliable and how expensive is the assistant, per pipeline, in this period", and "how do the RAG variants score offline, run after run".

**Non-Goals:** see the proposal.

## Decisions

### 1. Period and comparison
Every metrics endpoint takes `from` and `to` (UTC dates, `to` exclusive; default the last 30 days). The page also requests the previous period of the same length to show deltas. Daily buckets use the UTC date.

### 2. Percentiles from the period's durations
Counts, sums and averages are aggregated in the database. p50/p95 are computed in the Application service from the numeric columns of the period only (creation time, pipeline, success, duration, TTFT), with linear interpolation like PostgreSQL's `percentile_cont`. The period is capped at 366 days, so the projection stays small, and the same code works with the EF InMemory provider used by the tests. *Alternative:* raw `percentile_cont` SQL behind a port — rejected for now: more code, untestable without Postgres, and not needed at this volume.

### 3. Time to first token
`AiCallLogs.TimeToFirstTokenMs` (nullable double) is filled from `AiCallOutcome.TimeToFirstToken` for streamed calls; older rows and non-streamed calls stay null and are left out of TTFT percentiles.

### 4. Cost
Estimated cost per call = `LlmUsageMetrics.EstimateCost(provider, model, input, output)`; Ollama is 0. The summary returns cost per step and per pipeline, and cost per 1,000 answers. `AiCallLogs` gets a nullable `Provider` column (filled from `AiCallOutcome.Provider`) so each call is priced with the provider that served it, fallback included; older rows without it are priced by model name only. Models without a price count 0 and the response lists them as unpriced so the page can say so.

### 5. Ingestion per pipeline
For each `(DocumentId, Pipeline)` only the latest `IngestionMetrics` row counts. Failures come from `DocumentJobStates` (`BaselineStatus` / `HierarchicalStatus = Failed`, by `UpdatedAt` in the period). The section remains platform-wide (`PlatformAdmin`).

### 6. Evaluation results
`evaluation."EvaluationRuns"` (run id, start/end, git commit, seed, chat/embedding/judge models, query instruction, question count, dataset and corpus hashes, RAGAS version, imported at) and `evaluation."EvaluationPipelineResults"` (run id, pipeline, the four RAGAS means and standard deviations, hallucination rate, hit@1/3/5, MRR, no-answer detection rate, failed and missing samples, latency p50/p95 of total and TTFT, mean tokens per answer, indexing ms per document p50/p95, chunks per document, points). Platform-wide data: no tenant column.

The runner gets `--import {runId}`: it reads the files of the run, refuses a run without `summary.csv` (not scored yet) and upserts by run id, so re-scoring a run replaces its rows. *Alternative:* the Python scorer writes to Postgres — rejected, it would need its own connection string and a second schema owner; the runner already has the DbContext.

### 7. Charts
Chart.js 4 through `ng2-charts` standalone directives, lazy-loaded with the analytics route so the chat bundle does not grow. Colours come from CSS variables so light and dark themes work; each pipeline keeps the same colour in every chart.

### 8. Layout
Period selector → organization usage (cards, daily chart, per-pipeline table, cost by step) → platform ingestion per pipeline → offline evaluation (run selector, bar charts, history) → Arena (collapsed). Sections still follow the roles of the current spec.

## Risks / Trade-offs

- **Old rows without TTFT** → TTFT p50 shows "—" until there are streamed calls with it.
- **Cost depends on the price table** → unpriced models are listed instead of silently counting 0.
- **Few evaluation runs** → the history chart is hidden with fewer than two runs.

## Migration Plan

One migration adds the nullable column and the `evaluation` schema; no data is rewritten. The old response fields are kept until the new page ships in the same release.
