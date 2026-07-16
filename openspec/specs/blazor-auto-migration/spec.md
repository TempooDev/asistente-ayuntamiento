# Specification: Blazor Auto Migration (Server → Auto)

## Purpose
Migrate the Blazor Web frontend from Interactive Server render mode to Blazor Auto, enabling SSR on first load and WASM in subsequent visits, supporting client-side localStorage access for ephemeral state and reducing server-side circuit overhead for a public deployment.

## Requirements

### Requirement: Project SDK Migration to Blazor Auto
The Web project SHALL be reconfigured to support both server-side and WebAssembly render modes.

#### Scenario: First page load (SSR)
- **WHEN** a user visits the application for the first time
- **THEN** the page SHALL render server-side (SSR) with full content immediately visible
- **AND** the .NET WASM runtime SHALL begin downloading in the background

#### Scenario: Subsequent loads (WASM)
- **WHEN** the .NET WASM runtime is cached in the browser
- **THEN** the application SHALL run entirely in the browser
- **AND** no Blazor Server circuit SHALL be maintained on the server

### Requirement: Component Render Mode Update
All interactive components SHALL be updated from `@rendermode InteractiveServer` to `@rendermode InteractiveAuto`.

#### Scenario: Chat page in Auto mode
- **WHEN** `Home.razor` renders in WASM mode
- **THEN** the SignalR hub connection SHALL be established directly from the browser to the ApiService
- **AND** the chat streaming behavior SHALL be identical to the Server mode

### Requirement: Auth0 OIDC Compatibility with WASM
The OIDC authentication flow SHALL use PKCE and be fully compatible with Blazor WASM execution.

#### Scenario: Authentication state in WASM
- **WHEN** the WASM runtime takes over rendering
- **THEN** the Auth0 authentication state SHALL be serialized from the server pre-render and deserialized in WASM via `AddAuthenticationStateSerialization`
- **AND** the JWT token SHALL be available to make authenticated API calls

### Requirement: WASM-Safe Service Registration
All services used in interactive components SHALL be registered in both the server-side and client-side (WASM) DI containers.

#### Scenario: HttpClient in WASM
- **WHEN** the app runs in WASM mode
- **THEN** `ChatApiClient` and other HTTP clients SHALL use the browser's native `HttpClient` (via `IHttpClientFactory` configured in the WASM project)
- **AND** the base address SHALL be set to the ApiService URL

### Requirement: Cloudflare R2 Blob Storage Adapter
The blob storage layer SHALL use an S3-compatible client pointing to Cloudflare R2 in production and Azurite in development.

#### Scenario: Development environment
- **WHEN** the application runs with the Development environment variable
- **THEN** the `IBlobStorageRepository` SHALL connect to the local Azurite emulator via the Aspire connection string

#### Scenario: Production environment
- **WHEN** the application runs in production
- **THEN** the `IBlobStorageRepository` SHALL connect to Cloudflare R2 using an S3-compatible client (endpoint: `https://{accountId}.r2.cloudflarestorage.com`)
- **AND** credentials SHALL be loaded from environment variables, never from source code
