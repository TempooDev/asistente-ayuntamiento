# Specification: Admin Metrics Dashboard

## Purpose
Give administrators one Angular page, `admin/analytics` ("Métricas"), with the AI usage of their organization, the platform's ingestion cost and the results of the Question Arena. Each section is shown only to the role that may read its data, and the API authorizes every call itself.
## Requirements
### Requirement: Page Access by Admin Role
The route `admin/analytics` SHALL be guarded by `authGuard` and `roleGuard` and SHALL only open for users with `admin-plataforma` or `admin-organizacion`. The "Métricas" link SHALL only be shown to those users. The guard only improves the UX; the API SHALL authorize each data call with its own policy.

#### Scenario: Administrator opens the page
- **WHEN** a signed-in user with `admin-plataforma` or `admin-organizacion` navigates to `/admin/analytics`
- **THEN** the page SHALL open with the sections allowed for their roles

#### Scenario: Regular user
- **WHEN** a signed-in user without an admin role navigates to `/admin/analytics`
- **THEN** the guard SHALL redirect them to `/chat`

#### Scenario: Anonymous user
- **WHEN** a user who is not signed in navigates to `/admin/analytics`
- **THEN** they SHALL be sent to the login page

### Requirement: Organization AI Usage
For organization administrators the page SHALL show the AI usage summary of `GET /api/ai/metrics/summary?from&to` (policy `OrganizationAdmin`): answers (calls) with their success rate, succeeded and failed answers, answer latency p50 and p95, time to first token p50, input, output and total tokens, mean tokens per answer, estimated cost in EUR, and a breakdown per model and step (calls, total tokens, average latency, estimated cost). `AiCallLogs` SHALL store each call's pipeline and step (`Operation`: `answer`, `query_expansion`, `history_analysis`) and, for streamed calls, `TimeToFirstTokenMs`; calls made for a user (answers, query expansion, history analysis, both Arena sides) SHALL be stored. Call counts, success rate, latency percentiles, time to first token and tokens per answer SHALL cover answers only; token totals and cost SHALL include every step, and the breakdown SHALL label the step ("Respuesta", "Expansión de consulta", "Análisis de historial"). Percentiles SHALL be computed from the period's answers with linear interpolation (the definition of `percentile_cont`). Cost SHALL be estimated with the configured price table (`Ai:Pricing`); self-hosted models cost 0, and models without a price SHALL be listed as unpriced instead of silently counting 0. Calls with `Pipeline = evaluation` SHALL be excluded. Ingestion enrichment belongs to no tenant and SHALL NOT be stored in `AiCallLogs`. The summary SHALL be aggregated in the database from the tenant's `AiCallLogs` only (tenant query filter); it MUST NOT include platform-wide data such as `IngestionMetrics`. `successRate` SHALL be a percentage from 0 to 100 and the page SHALL show it as such (95 is "95 %"), never through a pipe that multiplies it by 100.

#### Scenario: Organization administrator
- **WHEN** a user with `admin-organizacion` opens the page
- **THEN** the "Uso del asistente" section SHALL show the summary of their own tenant's chat calls in the selected period and nothing from other tenants, from ingestion or from the evaluation runner

#### Scenario: Success rate
- **GIVEN** the tenant made 40 calls in the period and 38 succeeded
- **WHEN** the summary is shown
- **THEN** the "Respuestas" card SHALL show 40 answers, "95 % con éxito", 38 succeeded and 2 failed

#### Scenario: Latency percentiles
- **GIVEN** 100 answers in the period, 95 of them under 4 s and 5 between 10 and 20 s
- **WHEN** the summary is shown
- **THEN** p50 SHALL be under 4 s and p95 SHALL reflect the slow tail instead of an average

#### Scenario: Time to first token not recorded
- **WHEN** no answer of the period has `TimeToFirstTokenMs`
- **THEN** the card SHALL show "—" instead of 0

#### Scenario: Other steps
- **GIVEN** besides its answers the tenant's users caused query expansions and a history analysis
- **WHEN** the summary is shown
- **THEN** those calls SHALL add their tokens and cost to the totals and appear in the breakdown with their step, but SHALL NOT change the number of answers, the success rate or the latency figures

#### Scenario: Unpriced model
- **WHEN** a call of the period used a model of a paid provider without a price in `Ai:Pricing`
- **THEN** the summary SHALL list that model as unpriced and the page SHALL say that the cost is incomplete

#### Scenario: Platform administrator without the organization role
- **WHEN** a user with only `admin-plataforma` opens the page
- **THEN** the usage section SHALL NOT be requested or shown

#### Scenario: Summary request fails
- **WHEN** the summary request fails
- **THEN** the page SHALL show an error toast and leave the section empty instead of breaking

### Requirement: Platform Ingestion Cost
For platform administrators the page SHALL show the platform-wide ingestion of `GET /api/ai/metrics/ingestion?from&to` (policy `PlatformAdmin`) per pipeline, aggregated from `IngestionMetrics` counting only the latest row of each document and pipeline: documents ingested in the period, processing seconds per document p50 and p95, mean chunks per document, embedded tokens, LLM enrichment calls and enrichment tokens, and the documents whose pipeline status became `Failed` in the period (`DocumentJobStates`). Each figure SHALL have its own label, and enrichment calls SHALL NOT be labelled as embeddings.

#### Scenario: Platform administrator
- **WHEN** a user with `admin-plataforma` opens the page
- **THEN** the "Ingesta" section SHALL show one column per pipeline with the figures above

#### Scenario: Re-ingested document
- **GIVEN** a document was ingested three times by the baseline pipeline
- **WHEN** the ingestion figures are computed
- **THEN** it SHALL count as one document, with the figures of its latest ingestion

#### Scenario: Organization administrator without the platform role
- **WHEN** a user with only `admin-organizacion` opens the page
- **THEN** the ingestion section SHALL NOT be requested or shown, and the API SHALL answer 403 if called directly

### Requirement: Arena Results
For platform administrators the page SHALL show, at the end and collapsed by default, the Arena results of `GET /api/arena/analytics` (policy `PlatformAdmin`) for the selected period: total, completed and pending battles, and per pipeline the wins, losses, ties, "both bad" votes, win rate, average latency and average estimated tokens. The table SHALL be filterable by pipeline on the client. The aggregates SHALL cover battles of every tenant and SHALL never include battle content.

#### Scenario: Collapsed by default
- **WHEN** a platform administrator opens the page
- **THEN** the Arena section SHALL be collapsed and SHALL be requested only when it is expanded

#### Scenario: Filtering by date
- **WHEN** a platform administrator changes the period while the Arena section is expanded
- **THEN** the page SHALL request the analytics again with the period as `startDate` and `endDate` and show the new aggregates

#### Scenario: Win rate
- **WHEN** a pipeline took part in voted battles
- **THEN** its win rate SHALL be its wins divided by its decisive votes (wins plus losses); ties, "both bad" votes and pending battles SHALL NOT count in the denominator
- **AND** ties and "both bad" votes SHALL be shown in their own columns

#### Scenario: Organization administrator without the platform role
- **WHEN** a user with only `admin-organizacion` opens the page
- **THEN** the Arena section SHALL NOT be shown, and the API SHALL answer 403 if called directly

### Requirement: Period Filter
The page SHALL have one period selector (last 7 days, last 30 days, or custom start and end dates; default last 30 days) that applies to every section except the evaluation results. Every metrics request SHALL send `from` and `to` (UTC dates, `to` exclusive), and the API SHALL default to the last 30 days when they are missing. Cards SHALL show the change against the previous period of the same length.

#### Scenario: Changing the period
- **WHEN** an administrator selects "últimos 7 días"
- **THEN** the usage, ingestion and Arena sections SHALL be requested again for those 7 days and the previous 7 days, and each card SHALL show its value and the change in percent

#### Scenario: Invalid range
- **WHEN** a request has `from` after `to` or a range longer than 366 days
- **THEN** the API SHALL answer 400

### Requirement: Usage Charts and Per-Pipeline Table
For organization administrators the usage section SHALL show a daily chart of answers in the period with a line for the p95 answer latency of each day, and a table with one row per pipeline that answered in the period (`baseline`, `hierarchical`, `arena` with both Arena sides merged because a side is a random slot rather than a RAG pipeline, and `shadow`) with: answers, success rate, latency p50 and p95, time to first token p50, mean tokens per answer and estimated cost per 1,000 answers. Days without answers SHALL appear with 0 answers and no latency point.

#### Scenario: Daily chart
- **GIVEN** the tenant answered on 3 of the last 7 days
- **WHEN** the 7-day period is shown
- **THEN** the chart SHALL have 7 days, with 0 answers and no latency point on the other 4

#### Scenario: Pipelines compared
- **WHEN** the tenant has answers from both pipelines in the period
- **THEN** the table SHALL show one row for each, so their latency, tokens and cost can be compared side by side

