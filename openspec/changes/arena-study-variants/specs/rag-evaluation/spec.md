## MODIFIED Requirements

### Requirement: Operational Latency Report
The evaluation runner (`--latency-report --from --to`, results under `eval/latency/{from}_{to}/`) SHALL read, for a date range, the platform's stored telemetry (`AiCallLogs`, `ArenaBattles`, `IngestionMetrics`, `DocumentJobStates`) and write per pipeline: answer latency (mean, p50, p95) and time to first token of chat answers, Arena side latency per pipeline and battle origin (`Chat` or `Shadow`, see `question-arena`), ingestion time per document, and the share of failed LLM calls and failed ingestions. To make time to first token available outside the metrics backend, `AiCallLog` SHALL gain a nullable `TimeToFirstTokenMs`, filled for streamed calls with the same value recorded in `app.llm.time_to_first_token` and left null for non-streamed calls. The script SHALL connect with a read-only connection string from an environment variable, aggregate across tenants without reading or writing question, answer or battle text, and never print personal identifiers.

#### Scenario: Report for chapter 8
- **WHEN** the script runs for November 2026 against the development database
- **THEN** it SHALL write one CSV per measure with counts, mean, p50 and p95 per pipeline

#### Scenario: Chat and shadow battles apart
- **WHEN** the period has chat Arena battles and shadow-test battles of the same pipeline
- **THEN** the Arena side latencies SHALL be written in separate rows for each origin

#### Scenario: Missing connection string
- **WHEN** the environment variable with the connection string is not set
- **THEN** the script SHALL exit with a message naming the variable and SHALL NOT fall back to a default credential
