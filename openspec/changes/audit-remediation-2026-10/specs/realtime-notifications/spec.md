## ADDED Requirements

### Requirement: Typed Notification Payloads
Server-to-client notification events SHALL send one serialized object per event, matching a shared contract.
- `DocumentStatusChanged` SHALL carry `{ documentId: string, status: string, errorMessage?: string }`.
- `ScraperStateChanged` SHALL carry `{ isScraping: boolean, message: string }`.

#### Scenario: Document status changes
- **WHEN** the ingestion pipeline marks document `D1` as `Completed`
- **THEN** connected admin clients SHALL receive `DocumentStatusChanged` with `{ documentId: "D1", status: "Completed" }`
- **AND** the documents table in the Angular UI SHALL update the row of `D1` without a reload

### Requirement: Hub Authentication over WebSockets
The API SHALL accept the `access_token` query-string parameter for every hub path under `/hubs`, so that WebSocket and SSE transports authenticate for both `/hubs/chat` and `/hubs/notifications`.

#### Scenario: Notifications hub over WebSocket
- **WHEN** an authenticated client connects to `/hubs/notifications` using the WebSocket transport
- **THEN** the connection SHALL be authenticated and established over WebSocket, without falling back to long polling

#### Scenario: Notifications hub without token
- **WHEN** a client connects to `/hubs/notifications` without a token
- **THEN** the negotiation SHALL fail with `401 Unauthorized`

### Requirement: Tenant- and Role-Scoped Delivery
Hubs SHALL add each connection to the group `tenant:{tenantId}`, and to `role:admin` when the user has the `administrador` role. Notifications SHALL be sent to groups and SHALL NOT be broadcast with `Clients.All`.

#### Scenario: Scraper state change
- **WHEN** an administrator of `org_A` triggers a scrape
- **THEN** `ScraperStateChanged` SHALL be delivered only to the `role:admin` group

#### Scenario: Tenant-specific event
- **WHEN** an event concerns tenant `org_A`
- **THEN** connections of tenant `org_B` SHALL NOT receive it

### Requirement: Token Refresh and Idempotent Connections in the SPA
The Angular SPA SHALL create hub connections through a single factory whose `accessTokenFactory` obtains a fresh token on every (re)connection. `connect()` SHALL be idempotent while a connection is in progress, and all hub connections SHALL be stopped on logout.

#### Scenario: Reconnection after token expiry
- **GIVEN** a hub connection opened with a token that has since expired
- **WHEN** the connection drops and automatic reconnection starts
- **THEN** the client SHALL obtain a new token via `getAccessTokenSilently()` and reconnect successfully

#### Scenario: Concurrent connect calls
- **WHEN** two components call `NotificationService.connect()` while the first call is still connecting
- **THEN** exactly one `HubConnection` SHALL be created

#### Scenario: Logout
- **WHEN** the user logs out
- **THEN** all hub connections SHALL be stopped before redirecting to Auth0
