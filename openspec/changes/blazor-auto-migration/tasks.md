## 1. Create the WASM Client Project

- [x] 1.1 Create `src/AsistenteAyuntamiento.Web.Client/AsistenteAyuntamiento.Web.Client.csproj` with `Microsoft.NET.Sdk.BlazorWebAssembly` SDK, targeting `net10.0`, referencing `ServiceDefaults` — **files**: `src/AsistenteAyuntamiento.Web.Client/AsistenteAyuntamiento.Web.Client.csproj`
- [x] 1.2 Create `src/AsistenteAyuntamiento.Web.Client/Program.cs` as the WASM entry point: call `builder.RootComponents.Add<App>("#app")`, `AddAuthenticationStateDeserialization()`, and `AddClientServices()` — **files**: `src/AsistenteAyuntamiento.Web.Client/Program.cs`
- [x] 1.3 Create `src/AsistenteAyuntamiento.Web.Client/ServiceExtensions.cs` with static `AddClientServices(this IServiceCollection services)` registering `HttpClient` with base address from configuration and any other WASM-safe services — **files**: `src/AsistenteAyuntamiento.Web.Client/ServiceExtensions.cs`
- [x] 1.4 Add the `.Client` project to the solution file `AsistenteAyuntamiento.slnx` — **files**: `AsistenteAyuntamiento.slnx`

## 2. Update the Server Project for Blazor Auto

- [x] 2.1 Update `src/AsistenteAyuntamiento.Web/AsistenteAyuntamiento.Web.csproj` to add a `<ProjectReference>` to the `.Client` project and add the `AWSSDK.S3` NuGet package — **files**: `src/AsistenteAyuntamiento.Web/AsistenteAyuntamiento.Web.csproj`
- [x] 2.2 Update `src/AsistenteAyuntamiento.Web/Program.cs` to replace `AddInteractiveServerComponents()` with `AddInteractiveServerComponents().AddInteractiveWebAssemblyComponents()`, add `AddAuthenticationStateSerialization()`, call `AddClientServices()`, and add `AddInteractiveWebAssemblyRenderMode()` to `MapRazorComponents` — **files**: `src/AsistenteAyuntamiento.Web/Program.cs`
- [x] 2.3 Move interactive Razor components (`Home.razor`, `Counter.razor`, `Weather.razor`) from `src/AsistenteAyuntamiento.Web/Components/Pages/` to `src/AsistenteAyuntamiento.Web.Client/Pages/`; update `@using` directives accordingly — **files**: `src/AsistenteAyuntamiento.Web/Components/Pages/`, `src/AsistenteAyuntamiento.Web.Client/Pages/`
- [x] 2.4 Update `src/AsistenteAyuntamiento.Web/Components/App.razor` to remove `@rendermode` from root (render mode is set per-component) and verify `<HeadOutlet>` and `<Routes>` are correct — **files**: `src/AsistenteAyuntamiento.Web/Components/App.razor`
- [x] 2.5 Update `src/AsistenteAyuntamiento.Web/Components/Routes.razor` if `@rendermode` is declared there; replace with `InteractiveAuto` — **files**: `src/AsistenteAyuntamiento.Web/Components/Routes.razor`

## 3. Add `@rendermode InteractiveAuto` to Interactive Components

- [x] 3.1 Add `@rendermode InteractiveAuto` directive to `Home.razor` (and any other interactive page components that moved to the client project) — **files**: `src/AsistenteAyuntamiento.Web.Client/Pages/Home.razor`, `src/AsistenteAyuntamiento.Web.Client/Pages/Counter.razor`
- [x] 3.2 Verify `_Imports.razor` in the client project includes all necessary `@using` statements (`Microsoft.AspNetCore.Components.Authorization`, etc.) — **files**: `src/AsistenteAyuntamiento.Web.Client/_Imports.razor`

## 4. Auth0 OIDC WASM Integration

- [x] 4.1 Add Auth0 OIDC packages to the server project (`Auth0.AspNetCore.Authentication` or `Microsoft.AspNetCore.Authentication.OpenIdConnect`) and configure PKCE flow in `Program.cs` with `AddAuthenticationStateSerialization()` — **files**: `src/AsistenteAyuntamiento.Web/Program.cs`, `src/AsistenteAyuntamiento.Web/AsistenteAyuntamiento.Web.csproj`
- [x] 4.2 Add `Microsoft.AspNetCore.Components.WebAssembly.Authentication` to the client project and call `AddAuthenticationStateDeserialization()` in the WASM `Program.cs` — **files**: `src/AsistenteAyuntamiento.Web.Client/Program.cs`, `src/AsistenteAyuntamiento.Web.Client/AsistenteAyuntamiento.Web.Client.csproj`
- [x] 4.3 Configure `appsettings.json` (server) with Auth0 `Domain`, `ClientId`, `Audience` values; verify `appsettings.Development.json` uses dev Auth0 app credentials — **files**: `src/AsistenteAyuntamiento.Web/appsettings.json`, `src/AsistenteAyuntamiento.Web/appsettings.Development.json`

## 5. Blob Storage Infrastructure

- [x] 5.1 Create `IBlobStorageRepository` interface in the Application layer with `UploadAsync(string key, Stream content, string contentType)` and `GetUrlAsync(string key)` methods — **files**: `src/AsistenteAyuntamiento.ApiService/` or new Infrastructure project
- [x] 5.2 Implement `S3BlobStorageRepository` using `AWSSDK.S3` (`AmazonS3Client`): detect development vs. production by environment variable and set `ServiceURL`, `PathStyleAddressing`, `ForcePathStyle` accordingly — **files**: `src/AsistenteAyuntamiento.Web/Infrastructure/S3BlobStorageRepository.cs` (or Infrastructure project)
- [x] 5.3 Register `IBlobStorageRepository` → `S3BlobStorageRepository` in the server `Program.cs`, reading `BLOB_ACCESS_KEY_ID`, `BLOB_SECRET_ACCESS_KEY`, `BLOB_ENDPOINT` from environment variables; throw `InvalidOperationException` with a clear message if any are missing in non-development environments — **files**: `src/AsistenteAyuntamiento.Web/Program.cs`

## 6. AppHost and Build Verification

- [x] 6.1 Verify `src/AsistenteAyuntamiento.AppHost/Program.cs` references `AsistenteAyuntamiento.Web` correctly (no changes expected, but confirm the Aspire builder picks up the new `.Client` assembly automatically) — **files**: `src/AsistenteAyuntamiento.AppHost/Program.cs`
- [x] 6.2 Run `dotnet build src/AsistenteAyuntamiento.Web.Client` and confirm zero errors (validates all WASM-safe services are registered) — **files**: build output only
- [ ] 6.3 Run the Aspire AppHost (`dotnet run --project src/AsistenteAyuntamiento.AppHost`) and verify: first-load is SSR (check DevTools Network tab — no WASM download on first frame), and subsequent navigation shows WASM runtime loaded from cache — **files**: manual verification
