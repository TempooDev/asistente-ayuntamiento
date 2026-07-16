## Why

The current Blazor frontend runs in **InteractiveServer** mode exclusively, requiring a persistent SignalR circuit to the server for every user session. For a public-facing RAG assistant, this creates unnecessary server load and limits scalability. Migrating to **Blazor Auto** (SSR on first load, WASM on subsequent visits) eliminates server-side circuit overhead once the WASM runtime is cached, while preserving instant first-load rendering. The Auth0 OIDC integration also needs to be validated for PKCE/WASM compatibility before more interactive features (chat, streaming) are built on top.

## What Changes

- The Web project SDK and render mode are reconfigured from `InteractiveServer` to `InteractiveAuto`, adding a companion `.Client` WASM project
- All interactive components are updated from `@rendermode InteractiveServer` to `@rendermode InteractiveAuto`
- Auth0 OIDC authentication state is serialized server-side and deserialized in WASM via `AddAuthenticationStateSerialization` / `AddAuthenticationStateDeserialization`
- Services used in interactive components (e.g., `HttpClient`, `ChatApiClient`) are registered in both the server DI container and the WASM client DI container
- **BREAKING**: A new `.Client` project (`AsistenteAyuntamiento.Web.Client`) is created; shared service registrations must be split between server and client startup
- Blob storage layer is wired to Cloudflare R2 (production) and Azurite emulator (development) via an `IBlobStorageRepository` implementation using the AWS S3-compatible SDK

## Capabilities

### New Capabilities

- `blazor-auto-migration`: Full migration of the Blazor frontend from InteractiveServer to Blazor Auto, including Auth0 OIDC WASM compatibility, WASM-safe DI service registration, and Cloudflare R2 / Azurite blob storage adapter

### Modified Capabilities

- `blazor-auto-migration`: Updating existing spec to include the blob storage adapter requirement (R2 / Azurite) alongside the render-mode migration scenarios

## Non-goals

- Chat streaming over SignalR is not implemented in this change — it is addressed in the `signalr-integration` change
- User API key management (Infisical vault integration) is out of scope
- Mobile or PWA features are not targeted
- No changes to the ApiService or Worker projects

## Impact

- **`src/AsistenteAyuntamiento.Web/`**: `Program.cs`, `App.razor`, `Routes.razor`, all page/component `.razor` files, `.csproj` updated
- **New project `src/AsistenteAyuntamiento.Web.Client/`**: WASM entry point (`Program.cs`), shared service registrations, `.csproj` with `Microsoft.NET.Sdk.BlazorWebAssembly`
- **`src/AsistenteAyuntamiento.AppHost/`**: `Program.cs` updated to reference the new Web project correctly (no structural change expected)
- **Dependencies added**: `AWSSDK.S3` (or `Minio`) for blob storage; `Microsoft.AspNetCore.Components.WebAssembly` for the client project
- **Auth0**: OIDC PKCE flow already supported; requires `AddAuthenticationStateSerialization` on the server and `AddAuthenticationStateDeserialization` on the client
