# Specification: SignalR Real-time Integration

## Purpose
Stream chat answers to the Angular client and push live ingestion and scraper state to admin pages, over two SignalR hubs of the ApiService: `/hubs/chat` and `/hubs/notifications`.

## Requirements

### Requirement: Authenticated Hub Connections
Both hubs SHALL require an authenticated user with a tenant (Auth0 organization). Browsers cannot send headers on WebSockets, so the API SHALL accept the access token in the `access_token` query parameter for paths under `/hubs`.

#### Scenario: Connecting with a valid token
- **WHEN** the Angular client connects to `/hubs/chat` with `?access_token=<Auth0 access token>`
- **THEN** the connection SHALL be established for that user and tenant

#### Scenario: Token refresh on reconnection
- **WHEN** the connection drops and SignalR reconnects automatically
- **THEN** the client SHALL request a fresh access token from Auth0 (`accessTokenFactory` in `HubConnectionFactory`) instead of reusing the original one

#### Scenario: Fallback transport
- **WHEN** WebSockets are blocked (a corporate proxy) and SignalR falls back to server-sent events or long polling
- **THEN** every hub call and stream SHALL still run in the caller's tenant (`TenantHubFilter` binds it from the connection user, not from the HTTP context)

#### Scenario: Unauthenticated connection
- **WHEN** a client connects without a token, with an expired token, or without an organization
- **THEN** the hub SHALL reject the connection (401, or 403 without tenant)

### Requirement: Streamed Chat Answers
The chat hub SHALL stream the assistant's answer as it is generated, using `IAsyncEnumerable` server methods.

#### Scenario: Streaming an answer
- **WHEN** the client invokes `StreamMessage(sessionId, message)` on a session it owns
- **THEN** the hub SHALL stream text chunks of the answer
- **AND** the Angular `ChatStore` SHALL append them to the conversation, rendering at most once per animation frame

#### Scenario: Arena answers side by side
- **WHEN** the client invokes `StreamArenaMessage(sessionId, message)`
- **THEN** the hub SHALL first send `{ option: "SessionId", content: <battle id> }` and then chunks tagged `Alfa` or `Beta` as each pipeline produces them
- **AND** the client SHALL be able to vote with `VoteArenaMessage({ chatSessionId, battleId, winner })`

#### Scenario: Session that belongs to another user
- **WHEN** the client uses a session it does not own (another user, or the same user in another organization)
- **THEN** streaming into it SHALL return the single message `Error [NOT_FOUND]: …`, loading it SHALL return no messages, deleting it SHALL change nothing, and it SHALL NOT appear in `GetSessions`
- **AND** none of these SHALL reveal whether the session exists

### Requirement: Typed Errors Without Details
Hub failures SHALL reach the client as a stable code (`UNAUTHORIZED`, `NOT_FOUND`, `INVALID_REQUEST`, `INTERNAL`) and never include exception details; model failures inside a stream SHALL arrive as `[LLM_QUOTA_EXCEEDED]`, `[LLM_NOT_CONFIGURED]` or `[LLM_UNAVAILABLE]` messages.

#### Scenario: Internal error during a stream
- **WHEN** an unexpected exception happens while streaming
- **THEN** the client SHALL receive `Error [INTERNAL]: …` and the details SHALL only be written to the server logs
- **AND** the client SHALL show the message mapped from the code (`mapStreamError`)

### Requirement: Group-scoped Notifications
The notification hub SHALL never broadcast with `Clients.All`. Connections join `tenant:{id}` and, for administrators, `role:admin` in `OnConnectedAsync`. `DocumentStatusChanged` (`{ documentId, status }`) and `ScraperStateChanged` (`{ isScraping, message }`) concern platform-wide ingestion and SHALL be sent to `role:admin` only.

#### Scenario: Document processed by the worker
- **WHEN** the ingestion worker reports a new status for a document
- **THEN** the API SHALL forward it to the `role:admin` group
- **AND** connections of other tenants' regular users SHALL NOT receive it

#### Scenario: Unauthorized listener
- **WHEN** a user without the administrator role is connected
- **THEN** it SHALL NOT receive scraper or ingestion notifications meant for administrators
