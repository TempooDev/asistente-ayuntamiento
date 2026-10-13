# realtime-notifications Specification

## Purpose
Deliver ingestion and scraper notifications over SignalR (`/hubs/notifications`) only to the users allowed to see them, with the payload shapes the Angular client expects, and keep the SPA's hub connections authenticated and unique.

## Requirements

### Requirement: Typed Notification Payloads
Server-to-client events of `NotificationHub` SHALL send one serialized object (camelCase) per event:
- `DocumentStatusChanged`: `{ documentId: string, status: string, errorMessage?: string }`, where `status` is the document's summary `JobStatus`; `errorMessage` is part of the contract but is not filled by the server yet;
- `DocumentStatusesChanged`: an array of the same objects, at most 500 per event;
- `ScraperStateChanged`: `{ isScraping: boolean, message: string }`.

#### Scenario: Document status changes in a worker
- **WHEN** the ingestion pipeline marks document `D1` as `Completed` and that is its summary status
- **THEN** the Worker SHALL publish it to `document_notifications_exchange`, the API SHALL relay it, and connected platform administrators SHALL receive `DocumentStatusChanged` with `{ documentId: "D1", status: "Completed" }`
- **AND** the documents page SHALL update the row of `D1` without a reload

#### Scenario: Bulk enqueue
- **WHEN** an administrator enqueues 1,200 documents
- **THEN** the API SHALL send three `DocumentStatusesChanged` events (500, 500 and 200 changes) after the states are saved
- **AND** the SPA SHALL apply each event as one batch

#### Scenario: Manual scrape lifecycle
- **WHEN** a platform administrator triggers a manual scrape of BOE
- **THEN** `ScraperStateChanged` SHALL be sent with `{ isScraping: true, message: "Extrayendo BOE..." }`
- **AND** when it ends, with `{ isScraping: false, message: "" }` on success, or with the reason the scrape did not complete (the scraper's `ForceScrape` message, or "No se ha podido contactar con el scraper.")

### Requirement: Hub Authentication over WebSockets
The API SHALL read the `access_token` query-string parameter as the bearer token for every path under `/hubs`, so WebSocket and SSE transports authenticate for both `/hubs/chat` and `/hubs/notifications`. Both hubs SHALL require an authenticated user.

#### Scenario: Notifications hub over WebSocket
- **WHEN** an authenticated client connects to `/hubs/notifications` using the WebSocket transport
- **THEN** the connection SHALL be authenticated and established over WebSocket, without falling back to long polling

#### Scenario: Notifications hub without token
- **WHEN** a client connects to `/hubs/notifications` without a token
- **THEN** the negotiation SHALL fail with `401 Unauthorized`

### Requirement: Role-Scoped Delivery
`NotificationHub.OnConnectedAsync` SHALL add each connection to the group `tenant:{tenantId}` when the user has a tenant, and to `role:platform-admin` when the user has the `admin-plataforma` role. Ingestion and scraper events concern the shared knowledge base and SHALL be sent only to `role:platform-admin`, never with `Clients.All`. No event is sent to tenant groups yet, and `ChatHub` joins no groups.

#### Scenario: Scraper state change
- **WHEN** a platform administrator triggers a scrape
- **THEN** `ScraperStateChanged` SHALL be delivered only to the `role:platform-admin` group

#### Scenario: User without the platform role
- **WHEN** an organization administrator or a citizen is connected to `/hubs/notifications`
- **THEN** they SHALL NOT receive `DocumentStatusChanged`, `DocumentStatusesChanged` or `ScraperStateChanged`

### Requirement: Token Refresh and Idempotent Connections in the SPA
The Angular SPA SHALL create hub connections through `HubConnectionFactory`, one `ManagedHub` per hub path, whose `accessTokenFactory` calls `getAccessTokenSilently()` on every (re)connection and which uses automatic reconnection. The SignalR library and the connection SHALL be created lazily on the first `ensureConnected()`, which SHALL be idempotent while a connection is starting. `AuthFacade.logout()` SHALL stop every hub connection before redirecting to Auth0.

#### Scenario: Reconnection after token expiry
- **GIVEN** a hub connection opened with a token that has since expired
- **WHEN** the connection drops and automatic reconnection starts
- **THEN** the client SHALL obtain a new token via `getAccessTokenSilently()` and reconnect

#### Scenario: Concurrent connect calls
- **WHEN** two components call `NotificationService.connect()` while the first call is still connecting
- **THEN** exactly one `HubConnection` SHALL be created and started once

#### Scenario: Logout
- **WHEN** the user logs out
- **THEN** all hub connections SHALL be stopped before redirecting to Auth0
