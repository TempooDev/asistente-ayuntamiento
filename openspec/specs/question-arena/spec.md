# Specification: Question Arena (Blind A/B Testing)

## Purpose
Compare the baseline flat-chunk pipeline (`Baseline`) with the hierarchical pipeline (`Hierarchical`) blindly: the user sees two anonymous answers, "Alfa" and "Beta", and votes for one. The votes decide which pipeline answers the normal chat. There are two entry points: Arena mode inside the chat (any user) and the Arena page for organization administrators.

## Requirements

### Requirement: Concurrent Dual-Pipeline Execution
For each Arena question the system SHALL run both pipelines concurrently, each in its own DI scope, and measure the latency of each side independently.

#### Scenario: Arena mode in the chat
- **WHEN** a user with Arena mode on sends a message (hub method `StreamArenaMessage(sessionId, message)`)
- **THEN** the hub SHALL first send `{ option: "SessionId", content: <battle id> }` and then forward the chunks of each side tagged `Alfa` or `Beta` as they are produced, without one side waiting for the other

#### Scenario: Arena page comparison
- **WHEN** an organization administrator submits a query on the `/arena` page (`POST /api/arena/compare`, policy `OrganizationAdmin`)
- **THEN** the API SHALL run both pipelines with `Task.WhenAll` and return the battle id (`SessionId`), both answers, both latencies and the fragments each side used (`SourcesAlfa`, `SourcesBeta`)

#### Scenario: One side is slower
- **WHEN** Alfa finishes in 2 s and Beta in 8 s
- **THEN** the stored latencies SHALL be about 2 s and 8 s, and Alfa's chunks SHALL reach the client without waiting for Beta

#### Scenario: One side fails
- **WHEN** one pipeline throws while the other answers
- **THEN** the failure SHALL be logged and the other side SHALL still be delivered; on the Arena page the failed side's answer SHALL be a generic error text

#### Scenario: Arena request cannot start
- **WHEN** the chat session id is invalid or not the caller's
- **THEN** `StreamArenaMessage` SHALL fail the stream with a `HubException` (`Error [INVALID_REQUEST]` or `Error [NOT_FOUND]`), shown in both columns

### Requirement: Randomized Blind Presentation
The system SHALL randomly assign (50/50) which pipeline is "Alfa" and which is "Beta", and SHALL NOT reveal the assignment before the vote. Each battle SHALL be stored as an `ArenaBattle` with `LeftSystem`/`LeftResponse` for Alfa and `RightSystem`/`RightResponse` for Beta, both latencies, token counts estimated from the text length, and `Winner = Pending`.

#### Scenario: Chat battle stored
- **WHEN** both sides of a chat Arena stream have finished
- **THEN** the battle SHALL be saved with the battle id sent first in the stream and the trace id of the request

### Requirement: Vote Capture
A vote SHALL only be accepted from the user who owns the battle, within the owner's tenant. The winner SHALL be one of `Alfa`, `Beta`, `Tie` or `BothBad`, read case-insensitively with underscores ignored (so the Arena page's `ALFA`, `BETA`, `TIE`, `BOTH_BAD` are valid); any other value SHALL be rejected.

#### Scenario: Voting in the chat
- **WHEN** the user picks "Alfa", "Beta" or "Empate" under a chat Arena answer
- **THEN** the client SHALL call the hub method `VoteArenaMessage({ chatSessionId, battleId, winner })` with `Alfa`, `Beta` or `Tie`
- **AND** the battle SHALL store the winner, and the chosen answer (for Alfa or Beta) SHALL become the assistant message of the conversation
- **AND** the client SHALL replace the two answers with the chosen one, or show both for a tie

#### Scenario: Voting on the Arena page
- **WHEN** the administrator submits a vote with `POST /api/arena/vote`
- **THEN** the battle SHALL store the winner (`ALFA`, `BETA`, `TIE` or `BOTH_BAD`), the optional clarity and precision preferences (`ALFA`, `BETA` or `EQUAL`) and the optional comment

#### Scenario: Invalid winner
- **WHEN** a vote has a winner outside the accepted values (including `Pending`)
- **THEN** REST SHALL answer 400 (validation problem) and the hub SHALL throw `Error [INVALID_REQUEST]`

#### Scenario: Voting on another user's battle
- **WHEN** a user votes on a battle id owned by another user (same or another tenant)
- **THEN** REST SHALL answer 404 and the hub SHALL throw `Error [NOT_FOUND]`
- **AND** the battle SHALL NOT change and no answer SHALL be copied into the voter's conversation

#### Scenario: Unauthenticated vote
- **WHEN** a request without a valid JWT calls `POST /api/arena/vote`
- **THEN** the API SHALL respond `401 Unauthorized`

### Requirement: Post-Vote Architecture Reveal on the Arena Page
After a REST vote the API SHALL return which pipeline was Alfa and which was Beta (`AlfaSystem`, `BetaSystem`: `Baseline` or `Hierarchical`). The Arena page SHALL then show them, together with a collapsible panel with the fragments each side used. The chat Arena mode does not reveal the pipelines.

#### Scenario: Revealing the systems
- **WHEN** a vote on the Arena page is recorded
- **THEN** the page SHALL show the pipeline behind each assistant and its sources

### Requirement: Arena Battle Ownership and Tenant Scope
Every `ArenaBattle` SHALL store the `TenantId` and `UserId` (`sub`) of its creator and SHALL be subject to the tenant global query filter.

#### Scenario: Creating a battle
- **WHEN** an authenticated user starts an Arena comparison
- **THEN** the persisted `ArenaBattle` SHALL contain the current `TenantId` and the user's `sub` as `UserId`

#### Scenario: Reading another tenant's battle content
- **WHEN** any query runs in the context of another tenant
- **THEN** the tenant query filter SHALL exclude the battle (its query and responses are never returned)

#### Scenario: Platform-wide aggregates
- **WHEN** a platform administrator requests `GET /api/arena/analytics` (policy `PlatformAdmin`), or the chat decides the winning pipeline
- **THEN** the system MAY aggregate battles across tenants (an allow-listed `IgnoreQueryFilters` use)
- **AND** it SHALL return only aggregates (counts, win rates, latency, tokens), never battle content

Moving the analytics aggregation into the database, instead of loading the battles into memory, is tracked by audit task 5.10.
