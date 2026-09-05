# Specification: Admin Metrics Dashboard

## Purpose
Provide a secured administrative view to visualize ingestion costs, arena results, and readability metrics for ongoing monitoring and TFG data collection.

## Requirements

### Requirement: Secured Admin Access
The admin dashboard SHALL be accessible only to authenticated users with the Admin role.

#### Scenario: Authorized admin access
- **WHEN** an authenticated user with the `Admin` role navigates to `/admin/metrics`
- **THEN** the system SHALL display the metrics dashboard.

#### Scenario: Unauthorized access attempt
- **WHEN** an unauthenticated user or a user without the `Admin` role attempts to access `/admin/metrics`
- **THEN** the system SHALL return HTTP 401/403 and redirect to the login page.

### Requirement: Ingestion Cost Comparison View
The dashboard SHALL display a comparative view of processing costs between the baseline and hierarchical pipelines.

#### Scenario: Viewing cost comparison
- **WHEN** an admin views the cost comparison section
- **THEN** the dashboard SHALL display: total tokens embedded per pipeline, total LLM enrichment calls (hierarchical only), average processing duration per document per pipeline, and a bar chart comparing these metrics.

### Requirement: Arena Results View
The dashboard SHALL display the current state of the Question Arena evaluation.

#### Scenario: Viewing arena results
- **WHEN** an admin views the arena results section
- **THEN** the dashboard SHALL display: total battles conducted, win rate per system (with confidence interval), statistical significance indicator (p-value), and a breakdown of wins by criterion (clarity vs. precision).

### Requirement: Readability Comparison View
The dashboard SHALL display IFSZ readability scores comparing raw gazette text with generated responses from both pipelines.

#### Scenario: Viewing readability scores
- **WHEN** an admin views the readability section
- **THEN** the dashboard SHALL display average IFSZ scores for: raw gazette text, baseline pipeline responses, and new pipeline responses, with a visual indicator of readability level.

### Requirement: OpenTelemetry Custom Meters
The system SHALL emit custom OpenTelemetry metrics for real-time visibility in the .NET Aspire Dashboard.

#### Scenario: Emitting ingestion metrics
- **WHEN** a document is ingested by either pipeline
- **THEN** the system SHALL emit OpenTelemetry counter metrics for `tokens_embedded` and `chunks_generated`, and a histogram metric for `ingestion_duration_ms`, tagged by pipeline and gazette source.

#### Scenario: Emitting arena metrics
- **WHEN** a vote is recorded in the arena
- **THEN** the system SHALL emit an OpenTelemetry counter metric for `arena_votes`, tagged by winner system.
