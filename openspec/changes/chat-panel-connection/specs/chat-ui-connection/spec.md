## ADDED Requirements

### Requirement: Authenticated Real-Time Chat Connection
The system SHALL establish a secure real-time WebSocket connection between the Blazor frontend and the API, authenticated via Auth0 JWT.

#### Scenario: Connecting to the Chat Hub
- **WHEN** the chat panel component initializes
- **THEN** it SHALL establish a SignalR connection to the `/hubs/chat` endpoint
- **AND** the connection SHALL transmit the Auth0 access token (e.g. via `access_token` query parameter or bearer header)
- **AND** the backend SHALL validate the JWT and associate the connection with the user's `sub` and `org_id`

#### Scenario: Sending and receiving messages
- **WHEN** the user submits a message in the chat panel
- **THEN** the frontend SHALL send the message to the SignalR Hub
- **AND** the backend SHALL process the query
- **AND** the frontend SHALL listen for the Hub's real-time events to append the assistant's reply and any relevant citations to the chat UI.
