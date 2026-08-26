# Capability: Chat UI Connection

## Purpose
Establishes a robust, real-time connection between the Angular frontend and the backend API using SignalR, ensuring isolated chat sessions, persistent background processing, and secure communication.

## Requirements

### Requirement: Authenticated Real-Time Chat Connection
The system SHALL establish a secure real-time WebSocket connection between the Angular frontend and the API, authenticated via Auth0 JWT.

#### Scenario: Connecting to the Chat Hub
- **WHEN** the Angular `ChatService` initializes
- **THEN** it SHALL establish a SignalR connection to the `/hubs/chat` endpoint (routed via YARP Gateway)
- **AND** the connection SHALL transmit the Auth0 access token
- **AND** the backend SHALL validate the JWT and associate the connection with the user's identity.

### Requirement: Independent Background Streaming
The UI SHALL support asynchronous streaming of AI responses across multiple chat sessions simultaneously without UI locking or cross-contamination.

#### Scenario: Switching chats during generation
- **GIVEN** an active AI generation in Chat A
- **WHEN** the user switches the UI view to Chat B
- **THEN** the Angular frontend SHALL continue receiving SignalR chunks for Chat A in the background
- **AND** update Chat A's in-memory message cache (`sessionMessages`)
- **AND** NOT render Chat A's fragments inside Chat B's view
- **AND** instantly restore Chat A's visual generation state when the user switches back.
