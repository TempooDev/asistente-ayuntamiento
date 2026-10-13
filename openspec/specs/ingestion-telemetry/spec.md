# Specification: Ingestion Telemetry and Cost Tracking

## Purpose
Record the processing cost of every document ingested by the baseline and hierarchical pipelines in `IngestionMetrics`, and aggregate the Question Arena votes into per-pipeline win rates, so both pipelines can be compared quantitatively in the TFG thesis.

## Requirements

### Requirement: Ingestion Metrics Recording
Each pipeline SHALL record one `IngestionMetrics` row (`ingestion."IngestionMetrics"`) per successfully ingested document through `IIngestionMetricsService.TrackIngestionAsync`, with: `Pipeline` (`PipelineType`), `Bulletin` (`BulletinType`), `DocumentId`, `TotalTokensEmbedded`, `TotalLlmCalls`, `TotalLlmTokens`, `ProcessingDurationMs`, `ChunksGenerated` and `CreatedAt`. A failure to save the metric SHALL be logged and SHALL NOT fail the ingestion.

#### Scenario: Recording hierarchical ingestion metrics
- **WHEN** the BOE or BOJA processor finishes a document, including the vector sync
- **THEN** it SHALL record `Pipeline = Hierarchical`, the number of enrichment calls, the LLM tokens they used (as reported by the provider, or estimated at 4 characters per token), the embedded tokens (estimated at 4 characters per token of the enriched text), the number of fragments and the wall-clock duration

#### Scenario: Recording baseline ingestion metrics
- **WHEN** the baseline pipeline finishes a document, including the vector sync
- **THEN** it SHALL record `Pipeline = Baseline` with `TotalLlmCalls = 0`, `TotalLlmTokens = 0`, the embedded tokens estimated at 4 characters per token, the number of chunks and the wall-clock duration

#### Scenario: Document that fails
- **WHEN** a document fails before its vectors are synced
- **THEN** no `IngestionMetrics` row SHALL be recorded for that attempt

#### Scenario: Re-ingestion
- **WHEN** a document is ingested again
- **THEN** a new row SHALL be added and the previous rows kept

### Requirement: Arena Win-Rate Aggregation
`IArenaAnalyticsService.GetAnalyticsAsync`, exposed as `GET /api/arena/analytics` under `Policies.PlatformAdmin`, SHALL aggregate the `ArenaBattles` of every tenant (optionally between `StartDate` and `EndDate`) and return the total, pending and completed battles and, per `PipelineType`, its wins, losses, ties, win rate, average latency and average tokens. The win rate SHALL be wins divided by every voted battle the pipeline took part in, so ties and "both bad" votes count in the denominator.

#### Scenario: Computing win rates
- **GIVEN** the hierarchical pipeline took part in 10 voted battles: 6 wins, 2 losses, 1 tie and 1 "both bad"
- **WHEN** the analytics are requested
- **THEN** its `WinRate` SHALL be 0.6, `WinCount` 6, `LossCount` 2 and `TieCount` 1

#### Scenario: Pending battles
- **WHEN** some battles have no vote yet
- **THEN** they SHALL count in `PendingBattles` and in the average latency and tokens, but not in the win-rate denominator

### Requirement: Readability Score Calculation (IFSZ)
The Application layer SHALL provide `IReadabilityService.CalculateIfsz(text)`, which computes the Flesch-Szigriszt index for Spanish as `206.835 - 62.3 * (syllables / words) - (words / sentences)`, counting syllables with Spanish diphthong and triphthong rules. It is registered in DI but not yet called by any endpoint or pipeline.

#### Scenario: Calculating IFSZ
- **WHEN** `CalculateIfsz` receives a non-empty Spanish text
- **THEN** it SHALL return the index computed from its syllable, word and sentence counts

#### Scenario: Empty text
- **WHEN** `CalculateIfsz` receives an empty or whitespace text
- **THEN** it SHALL return 0
