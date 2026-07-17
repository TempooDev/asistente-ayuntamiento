## Context

The system currently responds to user chat queries using traditional HTTP request/response patterns. However, standard LLM interaction involves generating text token-by-token. Waiting for the complete response before returning it over HTTP introduces significant latency, degrading the user experience. Additionally, the RAG system retrieves source citations (documents from BOE, BOJA, BOPM) that should be displayed to the user as soon as they are resolved, even before the LLM begins streaming text. 

We need a persistent connection mechanism to handle these two requirements efficiently. The application already utilizes Blazor Auto (.NET 10), which makes SignalR a natural fit for real-time communication between the WASM client and the ASP.NET Core ApiService.

## Goals / Non-Goals

**Goals:**
- Provide a responsive chat experience by streaming the LLM's response character-by-character to the client.
- Deliver citation metadata immediately after similarity search resolves, before the LLM starts generating text.
- Establish a resilient connection between Blazor WASM and the ApiService.
- Ensure authentication (JWT tokens) works correctly over the SignalR connection.

**Non-Goals:**
- Persisting chat messages in the database (this is handled by a separate capability/spec `blazor-chat-history`).
- Real-time notifications for background tasks outside the active chat session.
- Rewriting the existing `IChatCompletionService` implementations.

## Decisions

### 1. SignalR over Server-Sent Events (SSE)
- **Decision**: Use SignalR instead of raw Server-Sent Events (SSE) or raw WebSockets.
- **Rationale**: SignalR provides built-in connection management, automatic reconnection, and fallback transports. It integrates seamlessly with ASP.NET Core auth and Blazor, avoiding boilerplate code. It also supports `IAsyncEnumerable<T>`, which maps perfectly to the Semantic Kernel streaming API.
- **Alternatives**: SSE is simpler for unidirectional streaming but requires manual connection management and makes sending the initial request (with complex payloads) over GET awkward. Raw WebSockets are too low-level and lack built-in RPC semantics.

### 2. Streaming via `IAsyncEnumerable<string>`
- **Decision**: The Hub method will return `IAsyncEnumerable<string>` for the text stream.
- **Rationale**: SignalR supports streaming from server to client by returning an `IAsyncEnumerable`. This perfectly matches the `GetStreamingChatMessageContentsAsync` signature from Semantic Kernel.
- **Alternative**: Pushing messages via `await Clients.Caller.SendAsync("ReceiveToken", token)` in a loop. Returning `IAsyncEnumerable` is cleaner, strongly typed, and handles backpressure better natively.

### 3. Out-of-band Citation Delivery
- **Decision**: The server will invoke a client-side method `ReceiveSources(Source[])` to push citations before returning the `IAsyncEnumerable` stream for the text.
- **Rationale**: The stream's return type is `string`. To send complex objects (citations) alongside strings without creating a complex union type (e.g., `ChatChunk` that can be either text or metadata), we use SignalR's bidirectional RPC to invoke the client method for metadata, then return the text stream.
- **Alternative**: Wrapper objects `record StreamChunk(string? Text, Source[]? Sources)`. This adds overhead to every single token sent. Pushing sources via RPC and then streaming text is more efficient.

### 4. Authentication Integration
- **Decision**: SignalR connection will inherit the `AccessToken` from the Blazor WASM `IAccessTokenProvider`.
- **Rationale**: The user is authenticated via Auth0. The SignalR client must pass the Bearer token during connection establishment so the `ChatHub` can identify the user (`Context.UserIdentifier`).

## Risks / Trade-offs

- **Risk**: Connection limits and resource usage. SignalR holds open connections.
  - **Mitigation**: Monitor connection counts. In a scalable deployment, we might need a Redis backplane if we scale the ApiService horizontally (though currently deployed on a single VPS).
- **Risk**: Token expiration during a long-lived SignalR connection.
  - **Mitigation**: SignalR handles token refresh if configured with an access token factory that requests a fresh token from Auth0 via the Blazor provider on every reconnect.
