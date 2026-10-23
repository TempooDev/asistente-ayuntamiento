## MODIFIED Requirements

### Requirement: Randomized Blind Presentation
Each battle SHALL pair the production variant (see `query-api-semantic-kernel`) with a challenger chosen uniformly among the other enabled pipelines (`Rag:ArenaPipelines`). The system SHALL randomly assign (50/50) which of the two is "Alfa" and which is "Beta", and SHALL NOT reveal the assignment before the vote. Each battle SHALL be stored as an `ArenaBattle` with `LeftSystem`/`LeftResponse` for Alfa and `RightSystem`/`RightResponse` for Beta, both latencies, token counts estimated from the text length, its origin (`Chat` for a battle a user started in the chat Arena, `Shadow` for a background shadow test; battles stored before this change count as `Chat`), and `Winner = Pending`. With the default `Rag:ArenaPipelines` the pair is always `Baseline` and `Hierarchical`, as before this change.

#### Scenario: Chat battle stored
- **WHEN** both sides of a chat Arena stream have finished without failing
- **THEN** the battle SHALL be saved with the battle id sent first in the stream, the trace id of the request and origin `Chat`

#### Scenario: Shadow battle stored
- **WHEN** a shadow test finishes in the background
- **THEN** its battle SHALL be saved with origin `Shadow`

#### Scenario: Challenger among the study variants
- **WHEN** `Rag:ProductionPipeline = Hierarchical` and `Rag:ArenaPipelines = Baseline,Hierarchical,Recursive,Semantic`
- **THEN** one side SHALL be `Hierarchical` and the other one of `Baseline`, `Recursive` or `Semantic`
- **AND** the stored `LeftSystem` and `RightSystem` SHALL name those pipelines

#### Scenario: Post-vote reveal with a study variant
- **WHEN** a user votes on a battle whose challenger was `Recursive`
- **THEN** the reveal on the Arena page SHALL name the recursive variant ("Fragmentación recursiva") instead of a generic baseline label

## ADDED Requirements

### Requirement: Arena Results per Variant
`GET /api/arena/analytics` (policy `PlatformAdmin`) and the Arena section of the admin metrics dashboard SHALL aggregate wins, losses, ties, "both bad", win rate, average latency and average tokens per `PipelineType` for every pipeline that appears in a battle (not only `Baseline` and `Hierarchical`), and SHALL also report the same figures per pair (production vs challenger), because battles are no longer always between the same two pipelines. The win-rate rules of `ingestion-telemetry` apply unchanged.

#### Scenario: Results with three challengers
- **WHEN** the dashboard is opened after battles of `Hierarchical` against `Baseline`, `Recursive` and `Semantic`
- **THEN** it SHALL show one row per pipeline and one row per pair with their counts and win rate

#### Scenario: Caller without the platform administrator role
- **WHEN** a user with only `admin-organizacion` calls `GET /api/arena/analytics`
- **THEN** the API SHALL answer `403 Forbidden` and return no aggregates
