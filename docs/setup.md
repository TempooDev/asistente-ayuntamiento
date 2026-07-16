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

> [!IMPORTANT]
> Docker Desktop debe estar **en ejecución** antes de arrancar la aplicación. Sin él, Aspire no puede levantar los contenedores de infraestructura.

---

### 2. Servicios de infraestructura (gestionados por Aspire en local)

Aspire descarga y arranca automáticamente los siguientes contenedores Docker en modo desarrollo. **No es necesario instalarlos manualmente.**

| Servicio | Imagen Docker | Propósito |
|---|---|---|
| **PostgreSQL + pgvector** | `pgvector/pgvector:pg16` | Base de datos principal + búsqueda vectorial |
| **RabbitMQ** | `rabbitmq:3-management` | Broker de mensajes (pipeline de ingesta) |
| **Azurite** | `mcr.microsoft.com/azure-storage/azurite` | Emulador local de Azure Blob Storage (equivalente a Cloudflare R2 en dev) |
| **Ollama** | `ollama/ollama` | Modelos de IA locales — embeddings (`nomic-embed-text`) y LLM de fallback (`llama3.2`) |

> [!TIP]
> La primera ejecución tarda varios minutos porque Docker descarga las imágenes y Ollama descarga los modelos. Las siguientes arrancan en segundos.

---

### 3. Servicios externos (SaaS — credenciales necesarias)

Estos servicios **no se emulan localmente** (o tienen emulación opcional) y requieren configuración manual.

#### Auth0 — Identity Provider

Gestiona la autenticación OIDC/OAuth2. Se necesita una aplicación Auth0 configurada.

1. Crear una cuenta en [auth0.com](https://auth0.com) (plan gratuito válido para dev)
2. Crear una **Regular Web Application**
3. En *Settings* de la aplicación, añadir a *Allowed Callback URLs*:
   ```
   https://localhost:<puerto>/callback
   ```
4. Añadir a *Allowed Logout URLs*:
   ```
   https://localhost:<puerto>/
   ```

Credenciales a configurar (ver sección de secrets más abajo):

| Variable | Dónde encontrarla |
|---|---|
| `Auth0:Domain` | Settings → Domain (ej. `mi-app.eu.auth0.com`) |
| `Auth0:ClientId` | Settings → Client ID |
| `Auth0:ClientSecret` | Settings → Client Secret |

#### Cloudflare R2 — Blob Storage (solo producción)

En **desarrollo**, Azurite emula el blob storage. R2 solo se necesita en producción.

Para producción, crear un bucket en [Cloudflare R2](https://developers.cloudflare.com/r2/) y configurar:

| Variable | Descripción |
|---|---|
| `Blob:Endpoint` | `https://<accountId>.r2.cloudflarestorage.com` |
| `Blob:AccessKeyId` | API token con permisos de lectura/escritura |
| `Blob:SecretAccessKey` | Secret del API token |
| `Blob:BucketName` | Nombre del bucket |

#### Infisical — Secrets Vault (solo producción)

En desarrollo se usa `InMemoryApiKeyVault` (sin contenedor adicional). En producción, Infisical almacena las API keys de los usuarios (OpenAI, Gemini, Anthropic, DeepSeek).

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

# ── Cloudflare R2 (opcional en dev — sin esto se usa Azurite automáticamente) ──
dotnet user-secrets set "Parameters:blob-endpoint"          "https://<accountId>.r2.cloudflarestorage.com" \
  --project src/AsistenteAyuntamiento.AppHost

dotnet user-secrets set "Parameters:blob-access-key-id"     "tu-r2-access-key" \
  --project src/AsistenteAyuntamiento.AppHost

dotnet user-secrets set "Parameters:blob-secret-access-key" "tu-r2-secret-key" \
  --project src/AsistenteAyuntamiento.AppHost

dotnet user-secrets set "Parameters:blob-bucket-name"       "nombre-del-bucket" \
  --project src/AsistenteAyuntamiento.AppHost
```

> [!NOTE]
> El formato `Parameters:<nombre>` es el estándar de Aspire para parámetros declarados con `builder.AddParameter("nombre")` en el AppHost.
> Si `blob-endpoint` no se configura, el sistema usa Azurite automáticamente — ideal para desarrollo sin credenciales de R2.

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
| **Frontend (Blazor)** | Enlace `webfrontend` en el dashboard |
| **API Service** | Enlace `apiservice` en el dashboard |
| **RabbitMQ Management** | Enlace `rabbitmq` en el dashboard |

---

## Comandos útiles

```bash
# Compilar todos los proyectos
dotnet build

# Compilar solo el cliente WASM (verificar que no faltan servicios)
dotnet build src/AsistenteAyuntamiento.Web.Client/

# Ejecutar tests (cuando existan)
dotnet test

# Ver los user-secrets configurados
dotnet user-secrets list --project src/AsistenteAyuntamiento.Web
```

---

## Estructura del proyecto

```
asistente-ayuntamiento/
├── src/
│   ├── AsistenteAyuntamiento.AppHost/        # Orquestador Aspire
│   ├── AsistenteAyuntamiento.ApiService/     # Backend REST API (ASP.NET Core)
│   ├── AsistenteAyuntamiento.Web/            # Frontend Blazor Auto (servidor)
│   ├── AsistenteAyuntamiento.Web.Client/     # Proyecto compañero WASM (Blazor Auto)
│   └── AsistenteAyuntamiento.ServiceDefaults/ # OpenTelemetry, health checks, service discovery
├── openspec/                                  # Planificación de cambios (OpenSpec)
│   ├── specs/                                 # Especificaciones por capacidad
│   └── changes/                               # Cambios en progreso
└── docs/                                      # Esta documentación
```

---

## Solución de problemas comunes

| Síntoma | Causa probable | Solución |
|---|---|---|
| `docker: Cannot connect to the Docker daemon` | Docker Desktop no está corriendo | Abrirlo y esperar a que esté activo |
| `Auth0:Domain configuration is missing` | Secrets no configurados | Ejecutar los `dotnet user-secrets set` del paso 3 |
| El modelo de Ollama no descarga | Sin conexión a internet o poco espacio | Verificar conexión; Ollama necesita ~2-4 GB por modelo |
| Puerto en uso al arrancar Aspire | Otro proceso ocupa el puerto | Cambiar el puerto en `Properties/launchSettings.json` del AppHost |
| Build error en `Web.Client` | Servicio server-only referenciado desde WASM | El cliente solo puede referenciar código WASM-compatible |
