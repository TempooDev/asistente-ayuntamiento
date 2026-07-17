## Why

The current API architecture uses standard request/response HTTP endpoints. For a generative AI chat experience, users expect real-time streaming of responses (character-by-character) and immediate delivery of context/citations before the text starts. SignalR provides a robust, bidirectional connection between the Blazor Auto client and the ASP.NET Core API service, enabling low-latency streaming and real-time metadata push.

## What Changes

- Add a SignalR Hub (`ChatHub`) to the `AsistenteAyuntamiento.ApiService`.
- Configure the Blazor WASM client to connect to `ChatHub` using `Microsoft.AspNetCore.SignalR.Client`.
- Implement server-to-client streaming of chat tokens as `IAsyncEnumerable<string>`.
- Implement a method to push citation metadata (sources) to the client prior to streaming the text response.
- Update the `Home.razor` (or a dedicated chat component) to consume the SignalR stream and update the UI incrementally.

## Capabilities

### New Capabilities
*(None)*

### Modified Capabilities
*(None - we are implementing the existing `signalr-integration` spec without changing its requirements).*

## Impact

- **Frontend**: The Blazor Auto client will establish persistent WebSockets/SignalR connections to the ApiService.
- **Backend**: The ApiService will require SignalR middleware.
- **Dependencies**: Need to add `Microsoft.AspNetCore.SignalR.Client` to the client project.
- **Infrastructure**: Aspire AppHost supports WebSockets by default.

## Non-goals

- Implementing full chat history persistence (covered by the `blazor-chat-history` spec).
- Changing the LLM provider interface (we will rely on the existing Semantic Kernel `IChatCompletionService` streaming support).
- Push notifications outside of the active chat session context.
