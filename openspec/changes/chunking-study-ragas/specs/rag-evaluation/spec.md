## ADDED Requirements

### Requirement: Fixed Corpus and Question Set
The evaluation SHALL use a versioned dataset under `eval/dataset/`: `corpus.txt` with the blob paths of the documents every variant ingests (`json/{SOURCE}/{id}.json`, BOE and BOJA only), and `questions.jsonl` with one question per line: `id`, `question` (in the words of municipal staff), `reference` (the expected answer), `reference_document_ids` (documents that answer it, all in the corpus) and `category` (for example `ayudas`, `urbanismo`, `empleo-publico`, `tributos`, `sin-respuesta`). Questions in `sin-respuesta` SHALL have an empty `reference_document_ids` and a reference saying that the bulletins do not answer it. A validation command SHALL fail when a question is malformed, an id is repeated or a referenced document is not in the corpus.

#### Scenario: Valid dataset
- **WHEN** `eval/ragas` validation runs on a dataset of 40 questions whose referenced documents are all in `corpus.txt`
- **THEN** it SHALL report 40 valid questions, the count per category and the corpus size, and exit with 0

#### Scenario: Reference outside the corpus
- **WHEN** a question references `BOE-A-2026-9999` and the corpus does not list it
- **THEN** validation SHALL name the question and the document and exit with a non-zero code

### Requirement: Evaluation Run per Pipeline
Every `IRagPipeline` SHALL implement `RunForEvaluationAsync`, which answers one single-turn question without streaming to a client and without storing chat messages, battles or shadow tests, and returns: the answer, the retrieved contexts in rank order (document id, score and text of the unit the search returned: a chunk for flat variants, a child fragment for the hierarchical one), the context text given to the generation model, input and output tokens, and the timings in milliseconds of query expansion (0 when the pipeline has none), retrieval, time to first token, generation and total. Model errors SHALL be returned as a typed error code in the result instead of thrown.

#### Scenario: Hierarchical evaluation run
- **WHEN** the hierarchical pipeline runs a question for evaluation
- **THEN** the result SHALL contain the fused top 5 child fragments as retrieved contexts and the full text of their parents as generation context
- **AND** `ExpansionMs`, `RetrievalMs`, `GenerationMs` and `TotalMs` SHALL be greater than 0 and `TotalMs` at least their sum

#### Scenario: No persistence
- **WHEN** a pipeline runs a question for evaluation
- **THEN** no `ChatMessage`, `ArenaBattle` or background shadow work SHALL be created

#### Scenario: Model failure
- **WHEN** the generation model is unreachable during an evaluation run
- **THEN** the result SHALL carry the `LLM_*` error code and an empty answer, and the runner SHALL record it as a failed sample

### Requirement: Evaluation Runner
`src/AsistenteAyuntamiento.Evaluation` SHALL be a console host, started explicitly from the Aspire AppHost or with `dotnet run`, that builds the same services as the API and, for each question and each pipeline selected with `--pipelines` (default the four), calls `RunForEvaluationAsync` in-process. It SHALL use one generation and ingestion model for every pipeline (`Evaluation:ChatModel`, default Ollama `qwen2.5:7b-instruct-q4_K_M`, temperature 0) and the platform embedding model (`qwen3-embedding:0.6b` with the query instruction on for the study). Before running it SHALL check that every selected collection exists, has the embedding model's vector size and contains points for every corpus document of its variant, and stop with a message naming what is missing. The order of the pipelines SHALL be shuffled per question with a seed. It SHALL write `eval/runs/{runId}/answers.jsonl` (one line per question and pipeline with the question id, pipeline, answer, contexts, generation context, tokens, timings and error code) and `run.json` (models, chunking settings, seed, corpus and dataset hashes, git commit, start and end time). The runner SHALL NOT expose any network endpoint.

#### Scenario: Complete run
- **WHEN** the runner is started with 40 questions and the four pipelines after the corpus is ingested by the four variants
- **THEN** `answers.jsonl` SHALL contain 160 lines and `run.json` SHALL record the models and the seed

#### Scenario: Variant not ingested
- **WHEN** `document_chunks_semantic` is missing or lacks points for some corpus documents
- **THEN** the runner SHALL stop before asking any question and list the variant and the missing document ids

#### Scenario: Embedding size mismatch
- **WHEN** `child_fragments` has 4,096-dimensional vectors and the configured embedding model produces 1024
- **THEN** the runner SHALL stop with an error naming the collection and both sizes

#### Scenario: Resuming a run
- **WHEN** the runner is restarted with `--resume {runId}`
- **THEN** it SHALL skip the question and pipeline pairs already in `answers.jsonl` and append the rest

### Requirement: RAGAS Scoring with a Local Judge
`eval/ragas/` SHALL contain a Python project (Python 3.12, dependencies pinned in a lock file) that reads a run and scores each answer with RAGAS using Ollama as judge (`EVAL_JUDGE_MODEL`, default the generation model) and the study embedding model (`qwen3-embedding:0.6b`) for embeddings: `LLMContextPrecisionWithReference` and `LLMContextRecall` on the retrieved contexts, `Faithfulness` on the generation context, and `ResponseRelevancy`. It SHALL also compute, without an LLM, the document hit rate at 1, 3 and 5 and the MRR of the retrieved contexts against `reference_document_ids`. Failed samples and judge errors SHALL be recorded as missing values, never as 0. It SHALL write `scores.csv` (one row per question and pipeline) and `summary.csv` (per pipeline: mean and standard deviation of each metric, hallucination rate = share of answers with faithfulness under `HALLUCINATION_THRESHOLD`, default 0.8, number of failed and missing samples), and the judge model and RAGAS version in `scoring.json`.

#### Scenario: Scoring a run
- **WHEN** the scorer runs on a run of 160 answers
- **THEN** `scores.csv` SHALL have 160 rows with the four RAGAS metrics, hit@1/3/5 and reciprocal rank
- **AND** `summary.csv` SHALL have one row per pipeline

#### Scenario: Question without an answer in the corpus
- **WHEN** a `sin-respuesta` question is scored
- **THEN** hit rate and reciprocal rank SHALL be missing for it, and the summary SHALL report separately the share of those questions where the answer says the bulletins do not cover it

#### Scenario: Judge unavailable
- **WHEN** Ollama is not reachable
- **THEN** the scorer SHALL stop with a message naming the Ollama URL and the judge model, without writing partial summaries

### Requirement: Latency and Indexing Cost Report
The evaluation SHALL produce `eval/runs/{runId}/latency.csv` with, per pipeline and stage (expansion, retrieval, time to first token, generation, total), the mean, p50 and p95 in milliseconds, excluding the first question of each pipeline (warm-up) and failed samples, and `indexing.csv` with, per pipeline, the corpus documents ingested, mean and p95 ingestion milliseconds per document, chunks per document, embedded tokens, LLM calls and LLM tokens (from `IngestionMetrics` of the corpus documents, the latest row per document and pipeline) and the number of points in its collection.

#### Scenario: Latency percentiles
- **WHEN** a run of 40 questions finishes for the four pipelines
- **THEN** `latency.csv` SHALL hold 39 samples per pipeline in its statistics and one row per pipeline and stage

#### Scenario: Semantic indexing cost
- **WHEN** the corpus was ingested by the semantic variant
- **THEN** its embedded tokens in `indexing.csv` SHALL include the sentence embeddings used to chunk, so its cost can be compared with the fixed variant

### Requirement: Operational Latency Report
A script under `eval/latency/` SHALL read, for a date range, the platform's stored telemetry (`AiCallLogs`, `ArenaBattles`, `IngestionMetrics`, `DocumentJobStates`) and write per pipeline: answer latency (mean, p50, p95) and time to first token of chat answers, Arena side latency, ingestion time per document, and the share of failed LLM calls and failed ingestions. To make time to first token available outside the metrics backend, `AiCallLog` SHALL gain a nullable `TimeToFirstTokenMs`, filled for streamed calls with the same value recorded in `app.llm.time_to_first_token` and left null for non-streamed calls. The script SHALL connect with a read-only connection string from an environment variable, aggregate across tenants without reading or writing question, answer or battle text, and never print personal identifiers.

#### Scenario: Report for chapter 8
- **WHEN** the script runs for November 2026 against the development database
- **THEN** it SHALL write one CSV per measure with counts, mean, p50 and p95 per pipeline

#### Scenario: Missing connection string
- **WHEN** the environment variable with the connection string is not set
- **THEN** the script SHALL exit with a message naming the variable and SHALL NOT fall back to a default credential
