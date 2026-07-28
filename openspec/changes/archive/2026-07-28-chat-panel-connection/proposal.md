## Why

The application currently has a basic profile system and authentication, but the core functionality—interacting with the AI assistant—lacks a user interface and a real-time connection. To allow municipal workers to query documents and chat with the AI, we need to build the chat panel UI in Blazor and establish a real-time connection (e.g., using SignalR) to the backend API.

## What Changes

- Implement the main Chat UI panel in the Blazor Auto frontend.
- Establish a real-time SignalR connection between the Blazor client and the ApiService.
- Ensure the connection securely passes the Auth0 JWT for authentication and tenant isolation.
- Create the necessary frontend components for displaying messages, inputting queries, and managing chat history.
- Set up the SignalR Hub on the backend to receive and push messages.

## Capabilities

### New Capabilities
- `chat-ui-connection`: Covers the frontend chat interface components and the real-time SignalR connection logic between the Blazor client and the backend API, including secure authentication over WebSockets.

### Modified Capabilities

## Impact

- **Frontend**: Adds new Blazor components and SignalR client dependencies.
- **Backend**: Adds SignalR Hubs to `AsistenteAyuntamiento.ApiService`.
- **Auth**: Requires JWT bearer token support for SignalR connections.

## Non-goals

- Implementing the actual LLM generation or vector search logic (this is handled by other ingestion/RAG components).
- Saving the chat history to the database (will be handled by a separate capability/spec for chat history).
