## Context

To provide a real-time conversational interface with the AI assistant, the Blazor Auto frontend requires a chat UI and a real-time connection to the backend `ApiService`. Since the application uses Auth0 for authentication and implements a multi-tenant B2B structure, the real-time connection must be secure and aware of the user's identity and tenant.

## Goals / Non-Goals

**Goals:**
- Design a scalable SignalR Hub architecture in the backend for real-time bidirectional communication.
- Ensure SignalR connections are authenticated using the Auth0 JWT bearer token.
- Design the frontend Blazor components (`ChatPanel.razor`, etc.) to interact with the SignalR Hub.

**Non-Goals:**
- Implementing the actual LLM generation or Semantic Kernel pipelines (this is handled separately).
- Persistent storage of chat history in the database (this will be handled by a separate Chat History capability).

## Decisions

1. **Transport Technology**: **SignalR**
   - *Rationale*: SignalR is the standard real-time library for ASP.NET Core and integrates seamlessly with Blazor. It automatically handles WebSocket negotiation, fallbacks, and reconnects.

2. **Authentication for SignalR**: **JWT Bearer Tokens**
   - *Rationale*: Since the frontend already uses Auth0 OIDC to obtain a JWT, we will pass this JWT to the SignalR Hub. Because WebSockets cannot easily send standard HTTP headers during the handshake in all environments, we will configure the SignalR client to send the `access_token` query parameter or configure the backend to accept it.
   - *Implementation detail*: In `Program.cs` of the API, we need to handle the `OnMessageReceived` event in the JWT Bearer options to extract the token from the query string if the request path starts with the Hub URL.

3. **Frontend Architecture**: **InteractiveAuto Render Mode**
   - *Rationale*: The Chat components must be highly interactive. Using `InteractiveAuto` allows them to be served via Server-Side Blazor for instant loading and then transition to WebAssembly. The SignalR connection logic must reside in the `Web.Client` project to work correctly under WebAssembly.

## Risks / Trade-offs

- **Risk**: Token expiration during a long-lived SignalR connection.
  - *Mitigation*: Blazor's authentication state provider and `AppTokenProvider` must handle token refresh. SignalR might need to reconnect if the token expires and the hub rejects further messages.
- **Risk**: WebSockets blocked by corporate firewalls.
  - *Mitigation*: SignalR gracefully falls back to Server-Sent Events or Long Polling.
