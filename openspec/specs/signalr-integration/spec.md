# Specification: SignalR Real-time Integration

## Purpose
Stream chat answers to the Angular client and push live ingestion and scraper state to admin pages, over two SignalR hubs of the ApiService: `/hubs/chat` and `/hubs/notifications` (reached through the YARP Gateway route `/hubs/{**catch-all}`).

## Requirements

### Requirement: Authenticated Hub Connections
Both hubs SHALL require an authenticated user with a tenant (Auth0 organization). Browsers cannot send headers on WebSockets, so the API SHALL accept the access token in the `access_token` query parameter for paths under `/hubs`. The Angular client SHALL create each hub connection lazily through `HubConnectionFactory` (one connection per hub, the SignalR library loaded on first use) and SHALL stop every connection before logging out.

#### Scenario: Connecting with a valid token
- **WHEN** the Angular client connects to `/hubs/chat` with `?access_token=<Auth0 access token>`
- **THEN** the connection SHALL be established for that user and tenant

#### Scenario: Token refresh on reconnection
- **WHEN** the connection drops and SignalR reconnects automatically
- **THEN** the client SHALL request a fresh access token from Auth0 (`accessTokenFactory` in `HubConnectionFactory`) instead of reusing the original one

#### Scenario: Fallback transport
- **WHEN** WebSockets are blocked (a corporate proxy) and SignalR falls back to server-sent events or long polling
- **THEN** every hub call and stream SHALL still run in the caller's tenant (`TenantHubFilter` binds it from the connection user's `org_id`, not from the HTTP context, and again before every streamed item)

#### Scenario: Unauthenticated connection
- **WHEN** a client connects without a token, with an expired token, or without an organization
- **THEN** the hub SHALL reject the connection (401, or 403 from `TenantRequiredMiddleware` without tenant)

### Requirement: Streamed Chat Answers
The chat hub SHALL stream the assistant's answer as it is generated, using `IAsyncEnumerable` server methods, and SHALL only operate on sessions owned by the caller in the caller's tenant.

#### Scenario: Streaming an answer
- **WHEN** the client invokes `StreamMessage(sessionId, message)` on a session it owns
- **THEN** the hub SHALL stream text chunks of the answer
- **AND** the Angular `ChatStore` SHALL append them to that session's messages, rendering at most once per animation frame

#### Scenario: Arena answers side by side
- **WHEN** the client invokes `StreamArenaMessage(sessionId, message)`
- **THEN** the hub SHALL first send `{ option: "SessionId", content: <battle id> }` and then chunks tagged `Alfa` or `Beta` as each pipeline produces them
- **AND** a side that fails SHALL end with a chunk `{ option: <side>, content: "", errorCode: <code> }`
- **AND** the client SHALL be able to vote with `VoteArenaMessage({ chatSessionId, battleId, winner })`, which returns the answer stored in the conversation

#### Scenario: Session that belongs to another user
- **WHEN** the client uses a session it does not own (another user, or the same user in another organization)
- **THEN** `StreamMessage` SHALL return the single message `Error [NOT_FOUND]: …`, `StreamArenaMessage` SHALL fail with that error, `LoadSession` SHALL return no messages, `DeleteSession` SHALL change nothing, and the session SHALL NOT appear in `GetSessions`
- **AND** none of these SHALL reveal whether the session exists

### Requirement: Typed Errors Without Details
Hub failures SHALL reach the client as a stable code (`UNAUTHORIZED`, `NOT_FOUND`, `INVALID_REQUEST`, `ALREADY_VOTED`, `INTERNAL`) and never include exception details, which SHALL only be written to the server logs. Errors detected before a chat stream starts SHALL be sent as the stream's single message; Arena set-up errors and failures in the middle of any stream (chat or Arena) SHALL be thrown as a `HubException` with `Error [INTERNAL]` (or the set-up code). Model failures before any text is produced SHALL arrive in the stream as `[LLM_QUOTA_EXCEEDED]`, `[LLM_NOT_CONFIGURED]` or `[LLM_UNAVAILABLE]` messages; a model failure after some text was streamed SHALL end the chat stream with a `HubException` carrying that `[LLM_*]` message.

#### Scenario: Model failure after some text
- **WHEN** the model stream fails (other than a cancellation by the client) after part of the answer was sent
- **THEN** the chat stream SHALL end with a `HubException` whose message is the typed `[LLM_*]` message (no provider details), never a silent end
- **AND** the client SHALL keep the partial answer and append the message mapped from the code (`mapStreamError`)

#### Scenario: Internal error during a stream
- **WHEN** an unexpected exception escapes the pipeline while streaming
- **THEN** the stream SHALL end with a `HubException` carrying `Error [INTERNAL]: …`, and the details SHALL only be logged
- **AND** the client SHALL keep the partial answer and append the message mapped from the code (`mapStreamError`)

#### Scenario: Connection lost during a stream
- **WHEN** the stream fails with an error that carries no known code
- **THEN** the client SHALL append the "connection lost" notice

### Requirement: Group-scoped Notifications
The notification hub SHALL never broadcast with `Clients.All`. Connections join `tenant:{id}` and, for platform administrators (`admin-plataforma`), `role:platform-admin` in `OnConnectedAsync`. `DocumentStatusChanged` (`{ documentId, status }`), `DocumentStatusesChanged` (an array of those, at most 500 per message) and `ScraperStateChanged` (`{ isScraping, message }`) concern platform-wide ingestion and SHALL be sent to `role:platform-admin` only. Status events published by the Worker SHALL reach every API instance through a RabbitMQ fanout exchange.

#### Scenario: Document processed by the worker
- **WHEN** the ingestion worker reports a new status for a document
- **THEN** each API instance SHALL forward it to its `role:platform-admin` connections
- **AND** connections of other users SHALL NOT receive it

#### Scenario: Unauthorized listener
- **WHEN** a user without the platform administrator role is connected
- **THEN** it SHALL NOT receive scraper or ingestion notifications

#### Scenario: Many documents enqueued at once
- **WHEN** an administrator enqueues or reprocesses hundreds of documents
- **THEN** their new states SHALL be sent as `DocumentStatusesChanged` batches instead of one message per document
- **AND** the documents page SHALL apply each batch in a single update
