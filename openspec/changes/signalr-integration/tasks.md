## 1. Backend: ApiService Configuration

- [ ] 1.1 Add `Microsoft.AspNetCore.SignalR` package (if not already implicit) and call `builder.Services.AddSignalR()` in `AsistenteAyuntamiento.ApiService/Program.cs` — **files**: `AsistenteAyuntamiento.ApiService/Program.cs`, `AsistenteAyuntamiento.ApiService/AsistenteAyuntamiento.ApiService.csproj`
- [ ] 1.2 Configure CORS or ensure the Web project can connect to the ApiService SignalR endpoint without blocking (Aspire defaults usually handle this) — **files**: `AsistenteAyuntamiento.ApiService/Program.cs`
- [ ] 1.3 Map the SignalR hub endpoint: `app.MapHub<ChatHub>("/chathub")` in `Program.cs` — **files**: `AsistenteAyuntamiento.ApiService/Program.cs`

## 2. Backend: ChatHub Implementation

- [ ] 2.1 Create `ChatHub.cs` inheriting from `Hub`. Implement an `IAsyncEnumerable<string> StreamChat(string message)` method — **files**: `AsistenteAyuntamiento.ApiService/Hubs/ChatHub.cs`
- [ ] 2.2 Inside `StreamChat`, invoke the Semantic Kernel `IChatCompletionService.GetStreamingChatMessageContentsAsync` and yield return each text chunk — **files**: `AsistenteAyuntamiento.ApiService/Hubs/ChatHub.cs`
- [ ] 2.3 Implement citation delivery: before yielding text, perform the RAG similarity search, gather the citations, and call `await Clients.Caller.SendAsync("ReceiveSources", sources)` — **files**: `AsistenteAyuntamiento.ApiService/Hubs/ChatHub.cs`
- [ ] 2.4 Add `[Authorize]` attribute to `ChatHub` and ensure the Hub can read `Context.UserIdentifier` (mapped from the Auth0 JWT `sub` claim) — **files**: `AsistenteAyuntamiento.ApiService/Hubs/ChatHub.cs`

## 3. Frontend: Client Project Setup

- [ ] 3.1 Add `Microsoft.AspNetCore.SignalR.Client` NuGet package to the `AsistenteAyuntamiento.Web.Client` project — **files**: `AsistenteAyuntamiento.Web.Client/AsistenteAyuntamiento.Web.Client.csproj`
- [ ] 3.2 Update `ServiceExtensions.cs` in the Client project to register a transient `HubConnection` builder or factory that automatically includes the access token from `IAccessTokenProvider` — **files**: `AsistenteAyuntamiento.Web.Client/ServiceExtensions.cs`

## 4. Frontend: UI Integration

- [ ] 4.1 Update `Home.razor` (or Chat component) to inject `HubConnection` and start it in `OnInitializedAsync` — **files**: `AsistenteAyuntamiento.Web.Client/Pages/Home.razor`
- [ ] 4.2 Register the `ReceiveSources` handler on the HubConnection to capture and store incoming citations before text arrives — **files**: `AsistenteAyuntamiento.Web.Client/Pages/Home.razor`
- [ ] 4.3 Replace the current HTTP chat send method with `HubConnection.StreamAsync<string>("StreamChat", message)`. Iterate with `await foreach` and update the UI incrementally (`StateHasChanged()`) — **files**: `AsistenteAyuntamiento.Web.Client/Pages/Home.razor`
- [ ] 4.4 Ensure proper disposal of the `HubConnection` when the component is disposed (`IAsyncDisposable`) — **files**: `AsistenteAyuntamiento.Web.Client/Pages/Home.razor`

## 5. Testing and Validation

- [ ] 5.1 Run the AppHost and verify that submitting a chat message triggers `ReceiveSources` first, followed by the text streaming into the UI — **files**: manual verification
- [ ] 5.2 Verify that the connection is authenticated (Server rejects unauthenticated connections) — **files**: manual verification
