# Specification: Ingestion Telemetry and Cost Tracking

## Purpose
Track and persist processing costs, token consumption, and latency metrics for both ingestion pipelines (baseline flat-chunk and hierarchical parent-child) to enable quantitative comparison in the TFG thesis.

## ADDED Requirements

### Requirement: Ingestion Metrics Recording
The system SHALL record processing metrics for each document ingested by either pipeline.

#### Scenario: Recording hierarchical ingestion metrics
- **WHEN** the BOE or BOJA ingestion service finishes processing a document
- **THEN** the system SHALL persist an `IngestionMetrics` record containing: pipeline identifier, gazette source, document ID, total tokens embedded, number of LLM enrichment calls, total LLM tokens consumed, wall-clock processing duration (milliseconds), and number of chunks generated.

#### Scenario: Recording baseline ingestion metrics (for comparison)
- **WHEN** the baseline flat-chunk pipeline processes a document for the arena
- **THEN** the system SHALL persist an `IngestionMetrics` record with the same schema, using pipeline identifier `BASELINE_FLAT`.

### Requirement: Readability Score Calculation (IFSZ)
The system SHALL calculate the Flesch-Szigriszt Index for Spanish text to compare the readability of raw gazette text vs. generated responses.

#### Scenario: Calculating IFSZ for a response
- **WHEN** a generated response or raw gazette text is submitted for analysis
- **THEN** the system SHALL count syllables (Spanish phonetic rules), words, and sentences
- **AND** compute `IFSZ = 206.835 - 62.3 * (syllables/words) - (words/sentences)`
- **AND** return the score and its interpretation (0–40: very difficult, 40–55: somewhat difficult, 55–65: normal, 65–80: somewhat easy, 80+: very easy).

### Requirement: Arena Win-Rate Aggregation and Statistical Testing
The system SHALL compute win rates from arena battles and test for statistical significance.

#### Scenario: Computing win rates
- **WHEN** the metrics export is triggered
- **THEN** the system SHALL query `arena_battles` and compute the percentage of battles won by each system (excluding ties and "both deficient" votes).

#### Scenario: Testing statistical significance
- **WHEN** win rates are computed
- **THEN** the system SHALL apply a binomial test to determine whether the new system's win proportion is significantly different from 0.5 (random chance) at p < 0.05.

### Requirement: Metrics Export for Thesis
The system SHALL export all metrics in CSV and JSON formats suitable for direct inclusion in the TFG document.

#### Scenario: Exporting metrics report
- **WHEN** an admin requests a metrics export via the API
- **THEN** the system SHALL generate files containing: win-rate table, p-value, IFSZ score distributions per pipeline, and ingestion cost comparison table (tokens, LLM calls, latency).
