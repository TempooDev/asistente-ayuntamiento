## Context

The current `AsistenteAyuntamiento.Web` project uses `Microsoft.NET.Sdk.Web` with `AddInteractiveServerComponents()` / `AddInteractiveServerRenderMode()` exclusively. All interactive Razor components run via a SignalR circuit maintained on the server. The project has no WASM counterpart.

The goal is to migrate to **Blazor Auto** render mode: SSR on first page load, then WASM once the .NET runtime is cached in the browser. This requires splitting the project into a server host and a WASM client project, wiring Auth0's OIDC state serialization, and ensuring all services used in interactive components work in both execution environments.

Additionally, the blob storage interface (`IBlobStorageRepository`) needs a concrete implementation backed by Cloudflare R2 (production) and Azurite (local dev), which is a prerequisite for the scraper and ingestion worker pipelines.

## Goals / Non-Goals

**Goals:**
- Migrate `AsistenteAyuntamiento.Web` from `InteractiveServer` to `InteractiveAuto`
- Create `AsistenteAyuntamiento.Web.Client` WASM project with shared service registrations
- Validate Auth0 OIDC state serialization across the SSR → WASM boundary
- Register all interactive-component services in the WASM DI container
- Implement `S3BlobStorageRepository` backed by Cloudflare R2 / Azurite

**Non-Goals:**
- SignalR chat streaming (covered in `signalr-integration` change)
- User API key management or Infisical vault integration
- Production Docker Compose changes
- Service Worker / PWA capabilities

## Decisions

### Decision 1: Companion `.Client` project (over single-project WASM)

**Chosen:** Create a separate `AsistenteAyuntamiento.Web.Client` project with `Microsoft.NET.Sdk.BlazorWebAssembly` SDK.

**Rationale:** Microsoft's recommended pattern for Blazor Auto is a server project that hosts both SSR and WASM, plus a companion `.Client` project that the WASM runtime downloads. The server project references the client project; shared services and components live in the client project. A single-project approach would require `net10.0-browser` multitargeting, which introduces build complexity and limits NuGet package compatibility.

**Alternatives considered:**
- _Single project with multitargeting_: Simpler scaffolding but more compile-time conditional guards (`#if !SERVER`) and harder to reason about; rejected.

---

### Decision 2: Auth0 OIDC with `AddAuthenticationStateSerialization`

**Chosen:** Use `AddAuthenticationStateSerialization()` on the server (inside `AddRazorComponents`) and `AddAuthenticationStateDeserialization()` in the WASM client `Program.cs`, alongside `Auth0WebAppWithAccessToken` package or OIDC cookie auth.

**Rationale:** When Blazor Auto transitions to WASM, the browser cannot access server-side cookies directly. `AddAuthenticationStateSerialization` serializes the `ClaimsPrincipal` from the server pre-render into a protected payload embedded in the HTML; the WASM runtime deserializes it without a round-trip. This is the official pattern from ASP.NET Core 8+.

**Alternatives considered:**
- _Pass JWT via localStorage_: Insecure (XSS risk); rejected.
- _Re-authenticate in WASM_: Would cause a visible redirect loop; rejected.

---

### Decision 3: AWSSDK.S3 for Cloudflare R2 (over Minio client)

**Chosen:** Use `AWSSDK.S3` (`AmazonS3Client`) configured with a custom `ServiceURL` pointing to Cloudflare R2 endpoint (`https://<accountId>.r2.cloudflarestorage.com`) or the Azurite emulator (`http://localhost:10000`).

**Rationale:** Cloudflare R2 is S3-compatible. AWSSDK.S3 is the most widely tested S3 client in .NET, has good support in Aspire via connection strings, and Azurite supports it natively with `PathStyleAddressing = true`. The Minio client is an alternative but adds a less-standard dependency.

**Alternatives considered:**
- _Minio .NET client_: Works but less conventional in .NET ecosystem; rejected in favor of AWS SDK.
- _Azure.Storage.Blobs against Azurite only_: Not compatible with Cloudflare R2 in production; rejected.

---

### Decision 4: Shared service registration via extension methods in `.Client`

**Chosen:** Define a static `AddClientServices(this IServiceCollection services)` extension in `AsistenteAyuntamiento.Web.Client`. Both the server `Program.cs` and the client `Program.cs` call this method.

**Rationale:** Avoids duplication of service registrations. The shared method registers services that must work in both SSR and WASM environments (e.g., `ChatApiClient`, `HttpClient`). Server-only services (e.g., OIDC middleware, blob storage) are registered separately.

## Risks / Trade-offs

| Risk | Mitigation |
|------|-----------|
| WASM bundle size increases first-load time | Defer WASM download; first load is SSR and already fast. Trim unused assemblies via `PublishTrimmed=true`. |
| Auth0 state serialization breaks across .NET upgrades | Pin package versions; add integration test that validates auth state in WASM mode. |
| `IBlobStorageRepository` not yet consumed by any UI | Implement as infrastructure-layer only; no UI wiring needed in this change. |
| Aspire connection string format differs between Azurite and R2 | Implement `IBlobStorageRepository` factory that reads `ASPIRE_BLOB_CONNECTIONSTRING` and detects the endpoint scheme. |
| Interactive components referencing server-only services break in WASM | Run `dotnet build AsistenteAyuntamiento.Web.Client` as a CI step to surface missing registrations early. |

## Migration Plan

1. Create `AsistenteAyuntamiento.Web.Client` project with WASM SDK
2. Move shared Razor components (`Home.razor`, `Counter.razor`, etc.) to the client project
3. Update `AsistenteAyuntamiento.Web.csproj` to reference the client project and add `AddInteractiveWebAssemblyComponents()`
4. Update `Program.cs` (server) to call `AddInteractiveWebAssemblyRenderMode()` and `AddAuthenticationStateSerialization()`
5. Create `Program.cs` in the client project with WASM entry point and `AddAuthenticationStateDeserialization()`
6. Implement `S3BlobStorageRepository` in Infrastructure layer; register conditionally in server `Program.cs`
7. Run `dotnet build` on both projects; verify no missing service registrations
8. Run the Aspire AppHost; verify SSR first load → WASM takeover in DevTools Network tab

**Rollback:** The change is additive until step 3. If WASM build breaks, revert the `.csproj` reference and `Program.cs` changes; the server project still compiles and runs in Server mode.

## Open Questions

- Should shared Razor components move to a shared class library (`AsistenteAyuntamiento.Web.Shared`) rather than the `.Client` project, for future SSR-only pages? → Deferred to `signalr-integration` or a separate refactor change.
- What Aspire connection string key will Cloudflare R2 credentials use in production? → Confirm with DevOps; placeholder env var `BLOB_CONNECTIONSTRING` used for now.
