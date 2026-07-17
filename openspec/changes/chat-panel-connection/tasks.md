## 1. Backend Hub Setup

- [x] 1.1 Install SignalR packages in `AsistenteAyuntamiento.ApiService` if not already present.
- [x] 1.2 Create `ChatHub.cs` in `ApiService/Features/Chat` with basic methods (`SendMessage`).
- [x] 1.3 Configure SignalR in `ApiService/Program.cs` and map `/hubs/chat` endpoint.
- [x] 1.4 Update JWT Authentication in `Program.cs` to extract `access_token` from query string for SignalR connections.

## 2. Frontend SignalR Client Setup

- [x] 2.1 Install `Microsoft.AspNetCore.SignalR.Client` in `AsistenteAyuntamiento.Web.Client`.
- [x] 2.2 Create a `ChatApiClient` or `ChatSignalRService` that initializes `HubConnectionBuilder` and injects `AppTokenProvider` for the access token.
- [x] 2.3 Register the `ChatSignalRService` in `ServiceExtensions.cs` or `Program.cs`.

## 3. Chat UI Components

- [x] 3.1 Create `ChatPanel.razor` page in `AsistenteAyuntamiento.Web` (or `Web.Client` for InteractiveAuto) under `/chat` route.
- [x] 3.2 Implement the message input and submit button in `ChatPanel.razor`.
- [x] 3.3 Implement the message list UI to display sent and received messages.
- [x] 3.4 Wire up `ChatPanel.razor` to use `ChatSignalRService` to send messages and subscribe to incoming message events.
