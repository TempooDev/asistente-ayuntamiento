## MODIFIED Requirements

### Requirement: Project SDK Migration to Blazor Auto
The Web project SHALL be reconfigured to support both server-side and WebAssembly render modes by splitting into a server host (`AsistenteAyuntamiento.Web`) and a WASM companion project (`AsistenteAyuntamiento.Web.Client`). The server project SHALL use `Microsoft.NET.Sdk.Web`; the client project SHALL use `Microsoft.NET.Sdk.BlazorWebAssembly`.

#### Scenario: First page load (SSR)
- **WHEN** a user visits the application for the first time
- **THEN** the page SHALL render server-side (SSR) with full content immediately visible
- **AND** the .NET WASM runtime SHALL begin downloading in the background

#### Scenario: Subsequent loads (WASM)
- **WHEN** the .NET WASM runtime is cached in the browser
- **THEN** the application SHALL run entirely in the browser
- **AND** no Blazor Server circuit SHALL be maintained on the server

#### Scenario: Client project builds independently
- **WHEN** `dotnet build AsistenteAyuntamiento.Web.Client` is executed
- **THEN** the build SHALL succeed with no errors
- **AND** all services referenced in interactive components SHALL be registered in the WASM DI container

### Requirement: Component Render Mode Update
All interactive components SHALL be updated from `@rendermode InteractiveServer` to `@rendermode InteractiveAuto`.

#### Scenario: Home page in Auto mode
- **WHEN** `Home.razor` renders in WASM mode
- **THEN** the component lifecycle (OnInitializedAsync, OnAfterRenderAsync) SHALL execute in the browser
- **AND** HTTP calls to the ApiService SHALL use the browser-native `HttpClient`

### Requirement: Auth0 OIDC Compatibility with WASM
The OIDC authentication flow SHALL use PKCE and be fully compatible with Blazor WASM execution. The server SHALL serialize the `ClaimsPrincipal` into the pre-rendered HTML; the WASM runtime SHALL deserialize it without a browser redirect.

#### Scenario: Authentication state in WASM
- **WHEN** the WASM runtime takes over rendering
- **THEN** the Auth0 authentication state SHALL be deserialized from the server pre-render payload via `AddAuthenticationStateDeserialization`
- **AND** the JWT access token SHALL be available to make authenticated API calls from WASM

#### Scenario: Unauthenticated user in WASM mode
- **WHEN** an unauthenticated user accesses a protected page in WASM mode
- **THEN** the application SHALL redirect to the Auth0 login page
- **AND** the PKCE flow SHALL complete and return the user to the originally requested URL

### Requirement: WASM-Safe Service Registration
All services used in interactive components SHALL be registered in both the server-side and client-side (WASM) DI containers via a shared `AddClientServices(IServiceCollection)` extension method.

#### Scenario: HttpClient in WASM
- **WHEN** the app runs in WASM mode
- **THEN** `ChatApiClient` and other HTTP clients SHALL use the browser's native `HttpClient` (via `IHttpClientFactory` configured in the WASM project)
- **AND** the base address SHALL be set to the ApiService URL resolved from configuration

#### Scenario: Server-only services not leaked to WASM
- **WHEN** the WASM client project is compiled
- **THEN** server-only services (OIDC middleware, blob storage, EF Core, Infisical) SHALL NOT be referenced by the client project
- **AND** the build SHALL complete without referencing server-only assemblies

## ADDED Requirements

### Requirement: Cloudflare R2 Blob Storage Adapter
The blob storage layer SHALL use an S3-compatible `AmazonS3Client` (AWSSDK.S3) pointing to Cloudflare R2 in production and the Azurite emulator in development. The implementation SHALL be registered as `IBlobStorageRepository` in the server-side DI container only.

#### Scenario: Development environment
- **WHEN** the application runs with the `Development` environment variable
- **THEN** the `IBlobStorageRepository` SHALL connect to the local Azurite emulator via the Aspire connection string
- **AND** `PathStyleAddressing` SHALL be set to `true` in the `AmazonS3Config`

#### Scenario: Production environment
- **WHEN** the application runs in production
- **THEN** the `IBlobStorageRepository` SHALL connect to Cloudflare R2 using the S3-compatible endpoint `https://<accountId>.r2.cloudflarestorage.com`
- **AND** credentials SHALL be loaded from environment variables (`BLOB_ACCESS_KEY_ID`, `BLOB_SECRET_ACCESS_KEY`), never from source code

#### Scenario: Missing blob credentials
- **WHEN** the required blob storage environment variables are absent at startup
- **THEN** the application SHALL throw a configuration exception during startup
- **AND** the error message SHALL identify which variable is missing
