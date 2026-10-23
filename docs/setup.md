# Setup & Run Guide

Guía completa de dependencias externas y arranque del proyecto **AsistenteAyuntamiento** — un sistema RAG orquestado con .NET Aspire.

---

## Dependencias externas

### 1. Herramientas de desarrollo (instalar una sola vez)

| Herramienta | Versión mínima | Notas |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | **10.0** | `dotnet --version` debe mostrar `10.x.x` |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | cualquier versión reciente | Requerido por Aspire para lanzar los contenedores |
| [.NET Aspire workload](https://learn.microsoft.com/aspire/fundamentals/setup-tooling) | 13.x | `dotnet workload install aspire` |
| [Node.js](https://nodejs.org) + npm | **22** / npm **11.6.2** | Frontend Angular (`packageManager` de `package.json`) |
| [Go](https://go.dev/dl/) | **1.25** | Scraper (`src/go-scraper`) |

> [!IMPORTANT]
> Docker Desktop debe estar **en ejecución** antes de arrancar la aplicación. Sin él, Aspire no puede levantar los contenedores de infraestructura.

---

### 2. Servicios de infraestructura (gestionados por Aspire en local)

Aspire descarga y arranca automáticamente los siguientes contenedores Docker en modo desarrollo. **No es necesario instalarlos manualmente.**

| Servicio | Imagen Docker | Propósito |
|---|---|---|
| **PostgreSQL** | `pgvector/pgvector:pg16` | Base de datos principal y búsqueda de texto completo |
| **Qdrant** | `qdrant/qdrant` | Base de datos vectorial (colecciones `document_chunks` y `child_fragments`) |
| **RabbitMQ** | `rabbitmq:3-management` | Broker de mensajes (pipeline de ingesta) |
| **MinIO** | `minio/minio` | Almacenamiento S3 local de los documentos del scraper (la API crea el bucket privado al arrancar) |
| **Ollama** | `ollama/ollama` | Modelos de IA locales — embeddings (`qwen3-embedding:0.6b`) y LLM de fallback (`llama3.2`) |

> [!TIP]
> La primera ejecución tarda varios minutos porque Docker descarga las imágenes y Ollama descarga los modelos. Las siguientes arrancan en segundos.

---

### 3. Servicios externos (SaaS — credenciales necesarias)

Estos servicios **no se emulan localmente** (o tienen emulación opcional) y requieren configuración manual.

#### Auth0 — Identity Provider

Gestiona la autenticación OIDC/OAuth2. Se necesita una aplicación Auth0 configurada.

1. Crear una cuenta en [auth0.com](https://auth0.com) (plan gratuito válido para dev)
2. Crear una **Single Page Application** para el frontend y una **API** (su *Identifier* es el `audience`)
3. Activar **Organizations** (cada organización es un tenant) y una Action que añada los roles en `https://asistente.antoniobermudez.dev/roles` (roles de administración: `admin-plataforma` para la base de conocimiento compartida y `admin-organizacion` para la organización)
4. En *Settings* de la SPA, añadir a *Allowed Callback URLs* `http://localhost:<puerto>/callback` y a *Allowed Logout URLs* / *Allowed Web Origins* `http://localhost:<puerto>`

El frontend no lleva la configuración compilada: la pide al arrancar a `GET /api/config/auth0`.

Credenciales a configurar (ver sección de secrets más abajo):

| Variable | Dónde encontrarla |
|---|---|
| `Auth0:Domain` | Settings → Domain (ej. `mi-app.eu.auth0.com`) |
| `Auth0:ClientId` | Settings → Client ID |
| `Auth0:ClientSecret` | Settings → Client Secret |
| `Auth0:Audience` | APIs → Identifier |

#### Cloudflare R2 — Blob Storage (solo producción)

En **desarrollo**, Aspire arranca MinIO y genera sus credenciales. R2 solo se necesita en producción (o si quieres probar contra él).

Para producción, crear un bucket en [Cloudflare R2](https://developers.cloudflare.com/r2/) y configurar:

| Variable | Descripción |
|---|---|
| `Blob:Endpoint` | `https://<accountId>.r2.cloudflarestorage.com` |
| `Blob:AccessKeyId` | API token con permisos de lectura/escritura |
| `Blob:SecretAccessKey` | Secret del API token |
| `Blob:BucketName` | Nombre del bucket |

#### Modelos de IA

Por defecto todo funciona con Ollama local (`llama3.2` para chat y `qwen3-embedding:0.6b` para embeddings). Los parámetros `ai-chat-*` fijan el modelo por defecto de la plataforma. Cada tenant puede elegir el suyo en *Configuración → IA*; su clave se guarda cifrada con Data Protection. Los embeddings son siempre los de la plataforma (`ai-embeddings-*`). En local, las consultas se embeben con la instrucción de tarea que recomienda Qwen3-Embedding (`Instruct: …\nQuery: …`): el texto está en `PromptCatalog.EmbeddingQueryInstruction` y se activa con `Ai:Embeddings:UseQueryInstruction=true`; los documentos se embeben sin ella. Desactivada (por defecto), las consultas se embeben tal cual. Para el modelo de respaldo ante cuota agotada se usa `Ai:Fallback:{Provider,Endpoint,ModelId,ApiKey}`.

---

## Configuración de secrets locales

Los secrets se configuran en el proyecto **AppHost** — no en los proyectos individuales. Aspire los lee y los inyecta como variables de entorno en cada servicio. En producción, el mismo mecanismo lee desde un backing store externo (Azure Key Vault, AWS Secrets Manager, etc.) sin cambiar el código.

```bash
# Navegar a la raíz del repositorio

# ── Auth0 ──────────────────────────────────────────────────────────────────────
dotnet user-secrets set "Parameters:auth0-domain"        "tu-tenant.eu.auth0.com" \
  --project src/AsistenteAyuntamiento.AppHost

dotnet user-secrets set "Parameters:auth0-client-id"     "tu-client-id" \
  --project src/AsistenteAyuntamiento.AppHost

dotnet user-secrets set "Parameters:auth0-client-secret" "tu-client-secret" \
  --project src/AsistenteAyuntamiento.AppHost

dotnet user-secrets set "Parameters:auth0-audience" "tu-api-audience" \
  --project src/AsistenteAyuntamiento.AppHost

# ── Modelos de IA (valores de ejemplo para Ollama local) ───────────────────────
dotnet user-secrets set "Parameters:ai-chat-provider"       "ollama"           --project src/AsistenteAyuntamiento.AppHost
dotnet user-secrets set "Parameters:ai-chat-model"          "llama3.2"         --project src/AsistenteAyuntamiento.AppHost
dotnet user-secrets set "Parameters:ai-chat-api-key"        ""                 --project src/AsistenteAyuntamiento.AppHost
dotnet user-secrets set "Parameters:ai-embeddings-provider" "ollama"           --project src/AsistenteAyuntamiento.AppHost
dotnet user-secrets set "Parameters:ai-embeddings-model"    "qwen3-embedding:0.6b" --project src/AsistenteAyuntamiento.AppHost
dotnet user-secrets set "Parameters:ai-embeddings-api-key"  ""                 --project src/AsistenteAyuntamiento.AppHost

# ── Cloudflare R2 (opcional en dev — sin esto se usa MinIO) ─────────────────────
dotnet user-secrets set "Blob:Endpoint"        "https://<accountId>.r2.cloudflarestorage.com" --project src/AsistenteAyuntamiento.AppHost
dotnet user-secrets set "Blob:AccessKeyId"     "tu-r2-access-key"  --project src/AsistenteAyuntamiento.AppHost
dotnet user-secrets set "Blob:SecretAccessKey" "tu-r2-secret-key"  --project src/AsistenteAyuntamiento.AppHost
dotnet user-secrets set "Blob:BucketName"      "nombre-del-bucket" --project src/AsistenteAyuntamiento.AppHost
```

> [!NOTE]
> El formato `Parameters:<nombre>` es el estándar de Aspire para parámetros declarados con `builder.AddParameter("nombre")` en el AppHost.
> Las credenciales de MinIO (`minio-root-*`) y la clave interna de gRPC (`internal-grpc-key`) las genera Aspire la primera vez y las guarda en los user-secrets; no hay que configurarlas.

> [!CAUTION]
> **Nunca** añadas secrets en `appsettings.json` ni en archivos que se suban al repositorio. `dotnet user-secrets` los guarda en `~/.microsoft/usersecrets/<id>/secrets.json`, fuera del árbol del proyecto.

---

## Cómo arrancar el proyecto

### Paso 1 — Clonar e instalar herramientas

```bash
git clone <repo-url>
cd asistente-ayuntamiento

# Instalar el workload de Aspire (solo la primera vez)
dotnet workload install aspire

# Restaurar dependencias NuGet
dotnet restore
```

### Paso 2 — Asegurarse de que Docker Desktop está en marcha

```bash
docker info   # debe devolver info del daemon, no un error
```

### Paso 3 — Configurar secrets (primera vez)

Seguir la sección [Configuración de secrets locales](#configuración-de-secrets-locales) arriba.

### Paso 4 — Arrancar con Aspire

```bash
dotnet run --project src/AsistenteAyuntamiento.AppHost/AsistenteAyuntamiento.AppHost.csproj
```

Aspire levantará todos los contenedores Docker y arrancará los servicios .NET. La salida del terminal mostrará una URL del **Aspire Dashboard** (normalmente `http://localhost:18888` o similar).

### Paso 5 — Acceder a la aplicación

Una vez que el dashboard muestre todos los servicios en verde:

| Servicio | URL |
|---|---|
| **Aspire Dashboard** | URL que aparece en el terminal al arrancar |
| **Frontend (Angular, vía Gateway)** | Enlace `gateway` en el dashboard |
| **API Service** | Enlace `apiservice` en el dashboard |
| **RabbitMQ Management** | Enlace `messaging` en el dashboard |
| **Scraper Go** | Enlace `go-scraper` (`/health`, `/api/scrape`, `/api/jobs/{id}`) |

---

## Comandos útiles

```bash
# .NET: compilar, comprobar formato y ejecutar tests (los de integración usan Docker vía Testcontainers)
dotnet build AsistenteAyuntamiento.slnx
dotnet format whitespace AsistenteAyuntamiento.slnx --verify-no-changes
dotnet test AsistenteAyuntamiento.slnx

# Angular
cd src/AsistenteAyuntamiento.Angular && npm ci && npx ng test --watch=false && npm run build

# Go (incluye la regeneración de protos con las versiones fijadas en CI)
cd src/go-scraper && go vet ./... && go test -race ./... && go generate ./internal/protos

# Ver los user-secrets configurados
dotnet user-secrets list --project src/AsistenteAyuntamiento.AppHost
```

---

## Migraciones de base de datos

Se conserva todo el historial de migraciones (hay datos en producción). La ApiService aplica las pendientes al arrancar (`Database:MigrateOnStartup`, `true` por defecto).

```bash
# Añadir una migración tras cambiar el modelo (no necesita base de datos)
dotnet ef migrations add <Nombre> --project src/AsistenteAyuntamiento.Infrastructure --startup-project src/AsistenteAyuntamiento.Infrastructure --output-dir Data/Migrations
```

- `dotnet ef` usa `AppDbContextFactory`, que lee la cadena de conexión de `ConnectionStrings__asistente-ayuntamiento-db` (o `DESIGN_TIME_CONNECTION_STRING`). Solo hace falta para comandos que tocan la base de datos (`database update`, `migrations list`).
- El test `MigrationAssemblyTests.The_model_matches_the_latest_migration_snapshot` falla si el modelo cambia sin su migración; ya no se suprime `PendingModelChangesWarning`.

---

## Estructura del proyecto

```
asistente-ayuntamiento/
├── src/
│   ├── AsistenteAyuntamiento.AppHost/         # Orquestador Aspire
│   ├── AsistenteAyuntamiento.Gateway/         # YARP: SPA + /api + /hubs
│   ├── AsistenteAyuntamiento.ApiService/      # Minimal APIs, SignalR, gRPC
│   ├── AsistenteAyuntamiento.Worker/          # Consumidores de ingesta (BASELINE / HIERARCHICAL)
│   ├── AsistenteAyuntamiento.Application/     # Casos de uso y puertos
│   ├── AsistenteAyuntamiento.Domain/          # Entidades (sin paquetes)
│   ├── AsistenteAyuntamiento.Infrastructure/  # EF Core, Qdrant, S3, RabbitMQ, Semantic Kernel
│   ├── AsistenteAyuntamiento.ServiceDefaults/ # OpenTelemetry, health checks, service discovery
│   ├── AsistenteAyuntamiento.Shared/          # Contratos compartidos
│   ├── AsistenteAyuntamiento.Angular/         # Frontend (core/ shared/ features/)
│   ├── go-scraper/                            # Scraper BOE/BOJA (cmd/scraper, internal/…)
│   └── Protos/                                # Contratos gRPC (C# y Go)
├── tests/                                     # Application, Infrastructure, ApiService, Architecture, Domain
├── openspec/                                  # Especificaciones (specs/) y cambios (changes/)
└── docs/                                      # Arquitectura, contratos, despliegue, auditoría
```

Más detalle en [architecture.md](architecture.md).

---

## Solución de problemas comunes

| Síntoma | Causa probable | Solución |
|---|---|---|
| `docker: Cannot connect to the Docker daemon` | Docker Desktop no está corriendo | Abrirlo y esperar a que esté activo |
| `Auth0:Domain configuration is missing` | Secrets no configurados | Ejecutar los `dotnet user-secrets set` del paso 3 |
| El modelo de Ollama no descarga | Sin conexión a internet o poco espacio | Verificar conexión; Ollama necesita ~2-4 GB por modelo |
| Puerto en uso al arrancar Aspire | Otro proceso ocupa el puerto | Cambiar el puerto en `Properties/launchSettings.json` del AppHost |
| La SPA muestra «No se ha podido cargar la configuración de inicio de sesión» | La API no responde en `/api/config/auth0` o faltan `auth0-*` | Revisar que `apiservice` está en verde y los parámetros de Auth0 |
| 403 en todas las llamadas a la API | El usuario no tiene organización (`org_id`) | Invitarle a una organización de Auth0 |
| `PRECONDITION_FAILED` al arrancar consumidores | Colas declaradas con otros argumentos | Borrar y recrear las colas (ver `docs/deploy-dokploy.md`) |
