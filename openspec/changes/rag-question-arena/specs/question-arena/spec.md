# Specification: Question Arena (Blind A/B Testing)

## Purpose
Provide a blind evaluation framework where users compare answers from the baseline flat-chunk pipeline and the new hierarchical pipeline side-by-side, without knowing which architecture powers which response.

## ADDED Requirements

### Requirement: Concurrent Dual-Pipeline Execution
The system SHALL execute both the baseline and new retrieval-generation pipelines concurrently for each arena query.

#### Scenario: Comparing responses
- **WHEN** a user submits a query via `POST /api/arena/compare`
- **THEN** the system SHALL execute both pipelines concurrently using `Task.WhenAll`
- **AND** return both responses with their respective latencies and a session ID.

### Requirement: Randomized Blind Presentation
The system SHALL randomize which pipeline's response appears as "Assistant Alfa" vs. "Assistant Beta" to eliminate presentation bias.

#### Scenario: Randomizing assignment
- **WHEN** both pipeline responses are ready
- **THEN** the system SHALL randomly assign (50/50 probability) which response is labeled "Alfa" and which is labeled "Beta"
- **AND** the pipeline identity SHALL NOT be revealed until after the user votes.

### Requirement: Structured Human Judgment Capture
The system SHALL collect a structured vote including overall preference, clarity comparison, precision comparison, and optional free-text feedback.

#### Scenario: Submitting a vote
- **WHEN** a user submits their judgment via `POST /api/arena/vote`
- **THEN** the system SHALL persist an `ArenaBattle` record with the de-randomized pipeline identities, the user's overall preference (Prefer Alfa / Prefer Beta / Tie / Both Deficient), sub-criterion votes (clarity, precision), and optional comment.

### Requirement: Post-Vote Architecture Reveal
The system SHALL reveal which architecture powered each assistant after the vote is submitted.

#### Scenario: Revealing the systems
- **WHEN** a vote is successfully recorded
- **THEN** the UI SHALL display which pipeline (BASELINE_6000 or NUEVO_HIBRIDO) powered each assistant
- **AND** provide a collapsible panel showing the actual source articles used by each pipeline.

### Requirement: Unauthenticated Arena Access
The system SHALL allow unauthenticated users to participate in the arena to maximize participation volume.

#### Scenario: Anonymous participation
- **WHEN** an unauthenticated user accesses the arena page
- **THEN** the system SHALL allow them to submit queries and votes without requiring login
- **AND** a unique `session_id` (UUID) SHALL be generated per comparison to link the query to its vote.
