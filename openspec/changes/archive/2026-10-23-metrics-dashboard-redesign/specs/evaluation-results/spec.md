## ADDED Requirements

### Requirement: Stored Evaluation Runs
Scored offline evaluation runs SHALL be stored in the schema `evaluation`: `EvaluationRuns` (run id as key, start and end time, git commit, seed, chat, embedding and judge models, query instruction, number of questions, dataset and corpus hashes, RAGAS version, import time) and `EvaluationPipelineResults` (run id and pipeline as key; mean and standard deviation of context precision, context recall, faithfulness and answer relevancy; hallucination rate; hit rate at 1, 3 and 5; MRR; no-answer detection rate; failed and missing samples; total latency and time to first token p50 and p95; mean tokens per answer; indexing milliseconds per document p50 and p95; chunks per document; points in the collection). These tables hold platform-wide data and SHALL have no tenant column. Missing metrics (for example a judge error for every sample) SHALL be stored as null, never as 0.

#### Scenario: Run with two pipelines
- **WHEN** a run scored for Baseline and Hierarchical is imported
- **THEN** there SHALL be one `EvaluationRuns` row and two `EvaluationPipelineResults` rows

### Requirement: Importing a Scored Run
`AsistenteAyuntamiento.Evaluation --import {runId}` SHALL read `run.json`, `summary.csv`, `latency.csv`, `indexing.csv` and `scoring.json` from `eval/runs/{runId}/` and upsert the run and its pipeline results by run id, replacing earlier rows of the same run. It SHALL stop with a message naming the missing file when the run has not been scored (`summary.csv` or `scoring.json` missing), and SHALL import nothing in that case.

#### Scenario: Re-scored run
- **WHEN** a run is scored again with another judge model and imported again
- **THEN** its rows SHALL be replaced and `EvaluationRuns` SHALL record the new judge model

#### Scenario: Run not scored
- **WHEN** `--import` is run on a run without `summary.csv`
- **THEN** the runner SHALL exit with a non-zero code naming `summary.csv` and SHALL NOT write to the database

### Requirement: Evaluation Results API and Section
`GET /api/evaluation/runs` SHALL list the stored runs (newest first, with their models and question count) and `GET /api/evaluation/runs/{runId}` SHALL return a run with its pipeline results; both under `Policies.PlatformAdmin`. For platform administrators the metrics page SHALL show an "Evaluación offline" section with a run selector (default the newest), grouped bar charts per pipeline for the four RAGAS metrics, for hit@k and MRR and for latency p50/p95, the hallucination rate and failed samples per pipeline, and, with two or more runs, a chart of each pipeline's faithfulness and context recall across runs. The section SHALL NOT depend on the period selector.

#### Scenario: Newest run shown
- **WHEN** a platform administrator opens the page and two runs are stored
- **THEN** the section SHALL show the newest run and the history chart with both runs

#### Scenario: No runs
- **WHEN** no run has been imported
- **THEN** the section SHALL explain how to import one (`--import {runId}`) instead of showing empty charts

#### Scenario: Evaluations unavailable
- **WHEN** the runs request fails
- **THEN** the section SHALL say inline that the evaluations are not available, without an error toast, and every other section SHALL load and show as usual
- **AND** a 404 (an API without the endpoint yet) SHALL be shown as the "no runs" state

#### Scenario: Loaded after the rest of the page
- **WHEN** the page opens
- **THEN** the section SHALL be requested only when it scrolls into view, after the period sections

#### Scenario: Organization administrator without the platform role
- **WHEN** a user with only `admin-organizacion` opens the page
- **THEN** the section SHALL NOT be shown, and the API SHALL answer 403 if called directly
