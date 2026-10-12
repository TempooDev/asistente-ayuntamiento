## MODIFIED Requirements

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

## REMOVED Requirements

### Requirement: Unauthenticated Arena Access
**Reason**: The implementation already requires authentication (`customAuthGuardFn` on the `arena` route, and `RequireAuthorization` on the arena endpoints). Anonymous battles cannot be owned, which makes the IDOR fix impossible.
**Migration**: None for users. Battles created before this change are assigned to tenant `legacy` and remain visible only in global analytics.

## ADDED Requirements

### Requirement: Arena Battle Ownership and Tenant Scope
Every `ArenaBattle` SHALL store the `TenantId` and `UserId` of its creator, and SHALL be subject to the tenant global query filter.

#### Scenario: Creating a battle
- **WHEN** an authenticated user starts an arena comparison
- **THEN** the persisted `ArenaBattle` SHALL contain the current `TenantId` and the user's `sub` as `UserId`

#### Scenario: Listing analytics as a tenant admin
- **WHEN** an administrator of tenant `org_A` requests arena analytics
- **THEN** only battles of `org_A` SHALL be aggregated
- **AND** the aggregation SHALL be computed in the database, without loading all battles into memory

### Requirement: Accurate Arena Metrics
Arena battles SHALL record the real latency and token usage of each side, measured independently while both pipelines run concurrently.

#### Scenario: One side is slower
- **GIVEN** pipeline Alfa produces its full answer in 2 s and pipeline Beta in 8 s
- **WHEN** the arena stream completes
- **THEN** the stored latency SHALL be about 2 s for Alfa and about 8 s for Beta, not 0
- **AND** Alfa chunks SHALL be delivered to the client without waiting for Beta chunks
