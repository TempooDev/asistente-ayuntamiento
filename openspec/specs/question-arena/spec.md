# Specification: Question Arena (Blind A/B Testing)

## Purpose
Provide a blind evaluation framework where users compare answers from the baseline flat-chunk pipeline and the new hierarchical pipeline side-by-side, without knowing which architecture powers which response.
## Requirements
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
The system SHALL collect a structured vote including overall preference, clarity comparison, precision comparison, and optional free-text feedback. A vote SHALL only be accepted from the user who owns the battle, within the owner's tenant.

#### Scenario: Submitting a vote
- **WHEN** the owner of a battle submits their judgment via `POST /api/arena/vote` or the hub method `ArenaVote`
- **THEN** the system SHALL persist the `ArenaBattle` vote with the de-randomized pipeline identities, the user's overall preference (Prefer Alfa / Prefer Beta / Tie / Both Deficient), sub-criterion votes (clarity, precision), and optional comment

#### Scenario: Voting on another user's battle
- **GIVEN** a battle with `SessionId = S` owned by user `u1` in tenant `org_A`
- **WHEN** user `u2` (same or another tenant) submits a vote or selects a winner for `SessionId = S`
- **THEN** the system SHALL respond as not found (`404`, or a hub error `NOT_FOUND`)
- **AND** SHALL NOT modify the battle or copy its responses into `u2`'s chat session

#### Scenario: Unauthenticated vote
- **WHEN** a request without a valid JWT calls `POST /api/arena/vote`
- **THEN** the API SHALL respond `401 Unauthorized`

### Requirement: Post-Vote Architecture Reveal
The system SHALL reveal which architecture powered each assistant after the vote is submitted.

#### Scenario: Revealing the systems
- **WHEN** a vote is successfully recorded
- **THEN** the UI SHALL display which pipeline (BASELINE_6000 or NUEVO_HIBRIDO) powered each assistant
- **AND** provide a collapsible panel showing the actual source articles used by each pipeline.

### Requirement: Arena Battle Ownership and Tenant Scope
Every `ArenaBattle` SHALL store the `TenantId` and `UserId` of its creator, and SHALL be subject to the tenant global query filter.

#### Scenario: Creating a battle
- **WHEN** an authenticated user starts an arena comparison
- **THEN** the persisted `ArenaBattle` SHALL contain the current `TenantId` and the user's `sub` as `UserId`

#### Scenario: Reading another tenant's battle content
- **GIVEN** a battle of tenant `org_A`
- **WHEN** any query runs in the context of tenant `org_B`
- **THEN** the tenant query filter SHALL exclude that battle (its query and responses are never returned)

#### Scenario: Platform-wide aggregates
- **WHEN** an administrator requests arena analytics, or the chat decides the winning pipeline for shadow testing
- **THEN** the system MAY aggregate battles across tenants (an allow-listed `IgnoreQueryFilters` use)
- **AND** it SHALL return only aggregates (counts, win rates, latency, tokens), never battle content
- **AND** analytics SHALL be restricted to the `Admin` policy

Moving the aggregation into the database, instead of loading all battles into memory, is tracked by audit task 5.10.

### Requirement: Accurate Arena Metrics
Arena battles SHALL record the real latency and token usage of each side, measured independently while both pipelines run concurrently.

#### Scenario: One side is slower
- **GIVEN** pipeline Alfa produces its full answer in 2 s and pipeline Beta in 8 s
- **WHEN** the arena stream completes
- **THEN** the stored latency SHALL be about 2 s for Alfa and about 8 s for Beta, not 0
- **AND** Alfa chunks SHALL be delivered to the client without waiting for Beta chunks

