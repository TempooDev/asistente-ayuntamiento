# Auditoría de código: asistente-ayuntamiento (octubre 2026)

**Alcance:** backend .NET (Domain, Application, Infrastructure, ApiService, Worker, Gateway, AppHost), frontend Angular 21, scraper Go, CI/CD y la higiene del repositorio.

**Estado del build:**
- `dotnet build`: compila sin errores, con 25 warnings.
- `go vet`: sin incidencias.
- `tsc` de la app: compila.
- `tsc` de los specs de Angular: **falla con 8 errores**.

Leyenda: 🔴 Crítico · 🟠 Alto · 🟡 Medio · ⚪ Bajo. Las rutas son relativas a `src/` salvo que se indique otra cosa.

---

## 1. Seguridad y multi-tenancy (lo primero que hay que tocar)

| # | Sev | Dónde | Problema | Arreglo |
|---|-----|-------|----------|---------|
| S1 | 🔴 | `ApiService/Program.cs`, todos los `*Endpoints.cs` | **No hay ninguna política de rol.** Cualquier usuario autenticado puede ejecutar `/api/ingestion/reset`, reprocesar todo, cambiar el proveedor o la API key de IA, gestionar filtros y lanzar el scraper. Los roles solo se comprueban en Angular, y la ruta `arena` ni siquiera tiene `roleGuardFn`. | `AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireClaim("{ns}/roles", "admin")))` y `.RequireAuthorization("Admin")` en los grupos de admin. Añadir `roleGuardFn` a `arena` en `app.routes.ts:32`. |
| S2 | 🔴 | `Application/Features/Chat/AiChatService.cs:260-269`, `:694-700` | El fallback ante errores 429 o de cuota envía **la API key del tenant (Google u OpenAI) a OpenRouter**. Además, el `catch` decide con `Message.Contains("limit")`, que casa con casi cualquier error. | Usar una clave de fallback propia (`Ai:Fallback:*`) y detectar el caso por código HTTP o tipo de excepción. |
| S3 | 🔴 | `AiChatService.cs:110`, `:558` | `ServerCertificateCustomValidationCallback = (...) => true` **sin condición de entorno**: se acepta cualquier certificado TLS en producción. | Eliminarlo y usar un `IHttpClientFactory` configurado en Infrastructure. |
| S4 | 🔴 | `go-scraper/internal/filterclient/client.go:35` | `InsecureSkipVerify: true` en el cliente gRPC de filtros. | Usar el pool de CAs del sistema. |
| S5 | 🔴 | `ApiService/Features/Tenants/CurrentTenantService.cs:14` | Sin `org_id`, el tenant pasa a ser `"default"`. Todos los usuarios sin organización comparten configuración de IA, logs y datos. | Responder 403 en producción. `"default"` solo en Development. |
| S6 | 🔴 | `Domain/Features/Arena/ArenaBattle.cs`, `ChatHub.cs:205-235`, `ArenaService.cs:97` | **IDOR**: `ArenaBattle` no tiene `TenantId` ni `UserId`. Con el GUID de sesión de otro usuario se puede votar su batalla y leer sus respuestas. | Añadir `TenantId` y `UserId`, `HasQueryFilter` y validar el propietario. |
| S7 | 🟠 | `UserPreferenceService.cs:11-59` | `IgnoreQueryFilters()` y filtra solo por `Auth0UserId`. | Añadir `&& TenantId == tenantId` explícito. |
| S8 | 🟠 | `SignalRNotificationService.cs:12`, `ScraperFilterEndpoints.cs:94,111` | `Clients.All`: las notificaciones llegan a **todos los tenants**. | Grupos por tenant en `OnConnectedAsync`. |
| S9 | 🟠 | `AiMetricsEndpoints.cs:14,77` | Las métricas y `RecentCalls` del singleton mezclan todos los tenants. | Filtrar por tenant y restringir a Admin. |
| S10 | 🟠 | `ChatHub.cs:75,120,184`, `IngestionEndpoints.cs` | Se devuelven `ex.GetType().Name` y `ex.Message` al cliente. | Quitar esos try/catch y dejar que actúe `UseExceptionHandler`. |
| S11 | 🟠 | `Infrastructure/DependencyInjection.cs:24-25`, `AppHost.cs`, `docker-compose.yml`, `.env.example` | Credenciales por defecto en el código (Blob, RabbitMQ). El bucket de MinIO se crea `public`. La API key de Qdrant está vacía. | Que la app falle al arrancar si falta un secreto. Bucket privado. |
| S12 | 🟡 | `ApiService/appsettings.Production.json:8-10` | Nivel Debug para Semantic Kernel en producción: puede registrar prompts con datos de ciudadanos. | Information/Warning. |
| S13 | 🟡 | NuGet | `Microsoft.OpenApi` 2.0.0 tiene una vulnerabilidad alta (GHSA-v5pm-xwqc-g5wc). | Actualizar. |

## 2. Bugs funcionales confirmados

| # | Sev | Dónde | Problema |
|---|-----|-------|----------|
| B1 | 🔴 | `go-scraper/main.go:209` | `contains()` termina en `\|\| true`: **el filtro por palabra clave aprueba todos los documentos.** |
| B2 | 🔴 | `AiChatService.cs:139,161` | `var kernel = kernelBuilder.Build()` oculta el `Kernel` inyectado. Después se pide el `IEmbeddingGenerator` a ese kernel local, que solo tiene chat registrado, así que `GetCompletionAsync` (usado por `ChatHub.cs:61`) falla con RAG. |
| B3 | 🔴 | `go-scraper/main.go:93-124`, `boja/client.go` | El gRPC `ForceScrape` no toma el mutex. Los `map` compartidos (`dateCache`), `SetSections` y `UpdateFeeds` pueden lanzar `fatal error: concurrent map writes`. Además, las secciones de un ForceScrape **se quedan fijas** para el cron. |
| B4 | 🔴 | Go `rabbitmq.go:91`; .NET `IngestionAdminService.cs:246,338` | Los mensajes se publican **sin persistencia**. Los consumidores hacen `BasicNack(requeue:false)` sin DLX declarado, así que los mensajes fallidos **se descartan**. |
| B5 | 🟠 | `SignalRNotificationService.cs:12` y `notification.service.ts:41` | El servidor envía `(documentId, status)` como dos argumentos y el cliente espera un objeto `{documentId, status}`. **El estado de los documentos nunca se actualiza en la UI.** |
| B6 | 🟠 | `ApiService/Program.cs:83` | El `access_token` de la query solo se acepta para `/hubs/chat`. `/hubs/notifications` responde 401 por WebSocket. |
| B7 | 🟠 | `chat.service.ts:71`, `notification.service.ts:32` | `accessTokenFactory: () => token` congela el token. Cuando caduca, la reconexión automática falla. |
| B8 | 🟠 | `DocumentIngestionService.cs:46,182,213` | `await notificationService?.X()` lanza `NullReferenceException` cuando el servicio es null. |
| B9 | 🟠 | `UserEndpoints.cs:99`, `HistoryAnalyzerService.cs` | `Task.Run` crea un scope sin `HttpContext`, el tenant pasa a `"default"` y el análisis de historial no encuentra nada. |
| B10 | 🟠 | `BoeIngestionService.cs:16`, `BojaIngestionService.cs:16` | Se usa siempre `DefaultBucketName` e ignora `CLOUDFLARE_R2_BUCKET_NAME`. |
| B11 | 🟠 | `DocumentIngestionService.cs:134-159`, `BaseHierarchicalIngestionProcessor.cs:69` | Al reingestar quedan **vectores huérfanos en Qdrant**: se escribe en Qdrant antes del commit de BD y se duplica el `ParentDocument`. |
| B12 | 🟠 | `FilterConfigServiceImpl.cs:31-35` | Ante un error, el gRPC devuelve **una lista de reglas vacía**, y el scraper lo interpreta como "sin filtros, descargar todo". |
| B13 | 🟠 | `go-scraper/main.go:184-206` | Si solo hay reglas `BojaFeed`, se descartan todos los documentos. |
| B14 | 🟡 | `AiChatService.cs:494,1138` | En la arena se guardan latencia y tokens a 0, lo que sesga las analíticas. |
| B15 | 🟡 | BOJA, BOPMA (Go y .NET) | `PublicationDate = DateTime.UtcNow // Stub`. BOPMA está registrado pero `FetchSummary` es un TODO. |
| B16 | 🟡 | Angular `perfil.ts:53`, `error.ts:20`, `arena.ts:40`, `documentos.ts:86`, `configuracion.ts:146` | Fugas de suscripciones, un `setInterval` que no se limpia, un spinner que no se apaga con error y búsqueda sin debounce (respuestas fuera de orden). |

## 3. Arquitectura y patrones de diseño

### 3.1 Clean Architecture rota en Application y Domain
- `Application.csproj` referencia `AWSSDK.S3`, `Qdrant.Client`, `RabbitMQ.Client`, `Pgvector.EntityFrameworkCore` y los conectores de SK para Google y Ollama.
- `HybridRetrievalService.cs:70-87` contiene SQL crudo con `NpgsqlParameter`.
- `IngestionAdminService` hace `TRUNCATE`, hace un cast a `DbContext` y declara colas de RabbitMQ.
- `IAppDbContext` expone `DatabaseFacade`.
- Domain referencia `Pgvector`, y `ChildFragment` usa `NpgsqlTsVector`.
- **Propuesta:** crear puertos en Application (`IVectorStore`, `IBlobStorage`, `IMessagePublisher`, `IFullTextSearch`, `IChatModelFactory`) con implementaciones en Infrastructure. Quitar los SDKs del csproj de Application. Mover el mapeo a `IEntityTypeConfiguration` y usar una shadow property para el `tsvector`.

### 3.2 God class: `AiChatService` (~1100 líneas, 11 dependencias)
- Mezcla seis responsabilidades:
  - construir el kernel por proveedor (duplicado en `:102-140` y `:550-588`);
  - RAG (copiado 3 veces);
  - montar los prompts;
  - streaming con fallback;
  - shadow testing;
  - arena y métricas.
- Usa la propia clase como service locator: `GetRequiredService<IAiChatService>() as AiChatService`.
- Lanza tareas fire-and-forget con `CancellationToken.None`.
- Crea un `HttpClient` nuevo en cada petición y nunca lo libera, lo que agota sockets.
- Le falta la declaración de `namespace`.
- **Propuesta:** dividirla en `IChatKernelFactory`, `IBaselineRagPipeline`, `IHierarchicalRagPipeline`, `IShadowTestRunner`, `IArenaStreamingService` e `IAiCallRecorder`. El trabajo en segundo plano pasaría por `Channel<T>` + `BackgroundService`.

### 3.3 Duplicación
- **Consumidores RabbitMQ:** `Infrastructure/.../RabbitMqConsumerService` y `Worker/.../HierarchicalRabbitMqConsumerService` comparten el ~90 % del código. Ninguno de los dos está muerto: se elige uno u otro con `WORKER_PIPELINE_MODE`. Propuesta: una base `RabbitMqConsumerBase<T>` con reconexión y DLQ.
- **Ingesta BOE y BOJA:** cada una descarga el XML dos veces, y BOJA trocea con `Substring(1000)` ignorando `IChunkingStrategy`.
- **Prompts:** hay dos catálogos (`Features/Chat/Prompts.cs` y `Common/Prompts/SystemPrompts.cs`) más prompts en línea en `AiChatService`, `ArenaService` y `ChatHub.cs:244` (este último en inglés).
- **Registro de DI:** se repite entre ApiService y Worker, y falta un `AddApplicationServices()`.
- **Go:** `scrapeDateRange` y `scrapeProviderDateRange` duplicadas. Solo BOE tiene reintentos; BOJA y BOPMA declaran un `rateLimiter` que nunca usan.
- **Angular:** la extracción de roles y `last_org_id` está copiada en 4 sitios, y la lógica de errores del chat aparece dos veces.

### 3.4 Lógica de negocio en la capa de presentación
- `AiMetricsEndpoints.cs:23-72` agrega en memoria tras cargar **tablas completas**.
- `UserEndpoints.GetOrCreateProfile` devuelve un perfil falso si hay un error.
- `ChatHub` accede directamente al `DbContext`.
- `FilterConfigServiceImpl` y `CurrentTenantService` viven en ApiService. El segundo debería estar en Infrastructure o en una capa Web común.
- Angular: `chat-panel.ts` muta los `Map` públicos del servicio y fuerza el refresco con `update(m => [...m])`. Hace falta un `ChatStore` con señales inmutables.

### 3.5 Consistencia de datos
- Hay doble escritura sin outbox: se publica en RabbitMQ antes de `SaveChanges` y se escribe en Qdrant antes del commit.
- `ChatPersistenceWorker` usa un buffer en memoria con un `Dictionary` que no es thread-safe, y descarta mensajes tras 3 reintentos.
- `ScraperStateService.IsScraping` se comprueba y se asigna sin atomicidad, y tampoco funciona con varias réplicas.

## 4. Organización e incongruencias

**Repositorio**
- **Binario de 34 MB versionado:** `src/go-scraper/go-scraper` (Mach-O). Hay que hacer `git rm --cached` y añadir la ruta a `.gitignore`. Para purgar el historial haría falta `git filter-repo`.
- **Ficheros sueltos en la raíz:** `patch_ingestion.patch` y `fix_docker.sh`.
- **Skills duplicadas** en `.agents/`, `.claude/`, `agent/` y `.github/skills` (auth0 aparece 3 veces y no son idénticas), además de `.agent/` y `.gemini/` para openspec.
- **`.gitignore`:**
  - Tiene comentarios en línea (`!.vscode/settings.json   # ...`), que git no admite, así que esas reglas no funcionan.
  - Se ve con mojibake (`â”€`), probablemente por un problema de codificación.
  - Ignora `appsettings.Development.json`, pero `Gateway/appsettings.Development.json` está versionado.
- **Go:**
  - Dos copias de los protos: `go-scraper/protos/` está obsoleta y `internal/protos/` es la que se usa. Falta un `go:generate`.
  - Sobran `cmd/validator`, que valida contra Azurite aunque el storage es S3, y `scrape-history.ps1`, que usa variables que ya no existen.
- **Ruta `Infrastructure/Infrastructure/Data`:** el namespace no coincide con la ruta, y `AddRagQuestionArena.cs:144,154` depende de ese nombre de recurso.
- **Migraciones con idas y vueltas:** Rename→Revert en 9 minutos, la columna vectorial se crea, se arregla y se borra el mismo día, y hay dos "Initial". Además, **`FixVectorDimensions` se editó después de aplicarse** (en 3 commits). Como no hay producción real, conviene consolidar en un baseline.
- **Restos de la plantilla:** `weather-route` en el Gateway, `/weatherforecast` en `ApiService.http`, `UnitTest1.cs` vacío y `Domain.Tests` sin ficheros.
- **Dockerfiles rotos y sin uso:** el deploy usa `PublishContainer`, y el de ApiService no copia los csproj de Application, Domain ni Infrastructure. O se borran o se alinean.

**Gestión de paquetes**
- No hay `Directory.Packages.props`. Las versiones están desalineadas: EF 10.0.10/11/12, OpenApi 10.0.1 frente a 10.0.10, Scalar, SK Qdrant 1.74-preview frente a SK 1.80, OTel 1.17 frente a 1.13, `Aspire.Hosting.NodeJs` 9.5.2 frente a 13.5.3.
- `NU1608`: Npgsql EF 9.0.1 se resuelve contra EF Core 10.
- Paquetes sin uso: `Azure.Storage.Blobs` en ApiService y `Pgvector` en Shared.
- `Directory.Build.props` solo tiene `NoWarn`. Falta `Nullable`, `TreatWarningsAsErrors` (o al menos para los avisos de nullability) y analizadores.

**Convenciones .NET**
- Carpetas mezcladas (`DTOs/`, `Models/` y DTOs sueltos) y varios tipos dentro del mismo fichero.
- Mezcla de español e inglés en nombres (`FiltroMunicipio`, `QueryLexica`, `Alfa/Beta`) y en logs.
- Logs interpolados (`$"..."`) y `Console.WriteLine` en varios sitios.
- Strings mágicos para estados, colas, colecciones de Qdrant y proveedores. Propuesta: enums, constantes e `IOptions`.
- `CancellationToken` no se propaga en `AiConfigurationService`, `ChatSessionService` ni `ScraperFilterService`.
- Rutas incoherentes: `/api/settings/ai`, `/api/config/auth0` y `/api/ai/metrics`.

**Convenciones Angular**
- Conviven `*ngIf`/`*ngFor` y `@if`/`@for`.
- Ningún componente usa `OnPush`.
- Mezcla de `Promise` y `Observable` en los servicios.
- Nombres de fichero incoherentes (`chat-panel.ts` frente a `welcome-guide.component.ts`) y `.scss` vacíos.
- Los DTO viven dentro de los servicios, hay `any` repartidos y `APP_INITIALIZER` (obsoleto) muta `environment`.
- Accesibilidad: `lang="en"`, botones de icono sin `aria-label`, modales sin `role="dialog"` y uso de `alert`/`confirm`.

**Go**
- `main.go` hace de orquestador con estado global.
- No hay apagado ordenado (signals, `Shutdown`, `GracefulStop`).
- No hay reconexión a RabbitMQ y el canal se comparte entre goroutines.
- Hay `defer resp.Body.Close()` dentro de bucles.
- Se mezclan `log`, `fmt` y `Println` en lugar de usar `slog`.
- El Dockerfile no fija versiones, corre como root y no tiene `.dockerignore`.

## 5. Tests y CI

- **Tests .NET:**
  - Solo hay 5 piezas de Application cubiertas, más 2 tests del publisher.
  - Sin tests: `AiChatService` (B2 se habría detectado), `HybridRetrievalService`, toda la ingesta, Arena, autorización y aislamiento entre tenants.
- **Angular:** los specs no compilan, porque importan símbolos que no existen (`ChatPanel`, `./chat`…).
- **Go:** ningún `_test.go`.
- **CI (`ci.yml`):**
  - No compila ni prueba Go.
  - No ejecuta los tests de Angular.
  - Usa Node 20 aquí y 22 en el deploy.
  - Usa `npm install` en lugar de `npm ci`.
- **Deploy:**
  - No depende de que pase el CI.
  - Solo etiqueta `:latest`, así que no hay forma de hacer rollback.
  - Posible desajuste entre el nombre de imagen que publica `deploy.yml:215` y el que espera `docker-compose.yml`. Hay que comprobarlo en GHCR.

---

## 6. Hoja de ruta propuesta

Cada fase puede ser un cambio de openspec.

1. **Hotfixes de seguridad y bugs** (pequeños y de alto impacto): S1–S6, B1, B2, B4 (mensajes persistentes y DLX), B5, B6, B7, B8, B12 y quitar `ex.Message` de las respuestas.
2. **Higiene del repositorio:**
   - sacar el binario de Go y arreglar `.gitignore`;
   - borrar el patch, `fix_docker.sh`, `protos/`, `cmd/validator`, `scrape-history.ps1`, `UnitTest1` y los restos de la plantilla;
   - unificar las carpetas de skills;
   - `Directory.Packages.props` y alinear las versiones;
   - CI con Go y Angular, y deploy condicionado al CI.
3. **Red de seguridad de tests:**
   - arreglar los specs de Angular;
   - tests de caracterización de los parsers BOE/BOJA (Go);
   - `HybridRetrievalService`, `ArenaAnalytics` y `ReadabilityService` (lógica pura);
   - integración con `WebApplicationFactory` + Testcontainers para autorización y tenant.
4. **Puertos en Application:** sacar Qdrant, S3, RabbitMQ y Npgsql de Application y Domain, aplanar `Infrastructure/Infrastructure`, consolidar las migraciones en un baseline y añadir `AddApplicationServices()`.
5. **Partir `AiChatService`** y crear un `RabbitMqConsumerBase<T>`. Outbox o publicación tras el commit, e ingesta idempotente con borrado en Qdrant.
6. **Go:** `internal/config`, un cliente HTTP compartido con rate limit y reintentos, providers sin estado mutable, `Orchestrator` con inyección de dependencias, apagado ordenado y `slog`.
7. **Angular:**
   - `AuthFacade` y una fábrica de hubs SignalR;
   - `ChatStore` y `MarkdownService`;
   - partir `chat-panel`, `documentos` y `configuracion`;
   - pasar a una estructura `core/ shared/ features/`;
   - migrar el control flow, `OnPush` y la accesibilidad.
8. **Convenciones:** idioma único en el código, logging estructurado, `CancellationToken` en toda la API, enums y opciones en lugar de strings mágicos, un tipo por fichero.
