## MODIFIED Requirements

### Requirement: Authenticated Real-Time Chat Connection
The system SHALL establish a secure real-time WebSocket connection between the Angular frontend and the API, authenticated via Auth0 JWT, and SHALL gracefully handle disconnections by auto-reconnecting upon user interaction.

#### Scenario: Connecting to the Chat Hub
- **WHEN** the Angular `ChatService` initializes
- **THEN** it SHALL establish a SignalR connection to the `/hubs/chat` endpoint (routed via YARP Gateway)
- **AND** the connection SHALL transmit the Auth0 access token
- **AND** the backend SHALL validate the JWT and associate the connection with the user's identity.

#### Scenario: Auto-reconnecting on message send
- **GIVEN** the SignalR connection has been lost (e.g. due to mobile background suspension)
- **WHEN** the user attempts to send a new message
- **THEN** the system SHALL transparently attempt to re-establish the connection before sending
- **AND** if successful, the message SHALL be sent without requiring a page reload.

## ADDED Requirements

### Requirement: Dynamic Chat Feedback and Smart Scrolling
The chat interface SHALL provide dynamic visual feedback during processing and manage scroll position intelligently to avoid disrupting the reading experience.

#### Scenario: Waiting for AI response
- **WHEN** the user sends a message and the system is waiting for the first chunk of the response
- **THEN** the UI SHALL display a dynamic, animated processing indicator (e.g., rotating text like "Processing message...", "Searching references...").

#### Scenario: Smart Auto-Scrolling during stream
- **GIVEN** a stream of incoming message chunks
- **WHEN** the user is already scrolled to the bottom of the chat window
- **THEN** the chat SHALL automatically scroll down as new chunks arrive.

#### Scenario: Preserving manual scroll during stream
- **GIVEN** a stream of incoming message chunks
- **WHEN** the user has scrolled up manually to read previous messages
- **THEN** the chat SHALL NOT automatically scroll down
- **AND** SHALL allow the user to read uninterrupted while the new message renders in the background.
