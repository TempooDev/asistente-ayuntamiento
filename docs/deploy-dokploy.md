# Guía de Despliegue con Dokploy y GHCR

Esta guía explica paso a paso cómo configurar GitHub y Dokploy para desplegar tu entorno de producción, utilizando el archivo `docker-compose.yml` optimizado para tu VPS.

## Paso 1: Generar el Token (PAT) en GitHub

Como tus imágenes Docker se subirán al registro privado de GitHub (GHCR), Dokploy necesita un permiso especial para poder descargarlas.

1. Ve a GitHub.com y entra en tu perfil arriba a la derecha > **Settings**.
2. Desciende en el menú de la izquierda y haz clic en **Developer settings**.
3. Selecciona **Personal access tokens** > **Tokens (classic)**.
4. Haz clic en el botón **Generate new token (classic)**.
5. Ponle un nombre descriptivo, por ejemplo: `Dokploy-Registry`.
6. En la lista de permisos (scopes), marca **SOLO** la opción: `read:packages`.
7. Genera el token y **cópialo**. (No lo volverás a ver).

## Paso 2: Añadir el Registro Docker en Dokploy

1. Accede al panel web de tu VPS con **Dokploy**.
2. Ve a la pestaña **Settings** (arriba a la derecha).
3. Selecciona la subpestaña **Docker Registries**.
4. Rellena los datos así:
   - **Name**: `GitHub Container Registry`
   - **Registry URL**: `ghcr.io`
   - **Username**: Tu nombre de usuario de GitHub.
   - **Password**: Pega aquí el Token (PAT) que copiaste en el paso anterior.
5. Haz clic en **Save** / **Add Registry**.

## Paso 3: Desplegar el Docker Compose en Dokploy

1. En Dokploy, entra en tu **Proyecto** (si no tienes uno, créalo) y dale a **Create Application**.
2. Elige el tipo **Compose**.
3. Ponle un nombre, por ejemplo: `ayuntamiento-prod`.
4. En el campo donde se pega el archivo `docker-compose.yml`, pega el código que encontrarás más abajo.
5. **MUY IMPORTANTE**: En los selectores de arriba, asegúrate de marcar tu registro `GitHub Container Registry` para que Dokploy sepa usar esas credenciales al tirar de `ghcr.io`.
6. Haz clic en **Deploy**. Dokploy descargará las imágenes y levantará toda la arquitectura (MinIO, RabbitMQ, Web, API, Worker, Gateway, y el Scraper).

---

## Archivo `docker-compose.yml` para Dokploy

Asegúrate de reemplazar las siguientes palabras clave antes de desplegar:
- `TU_USUARIO_GITHUB`: Cámbialo por tu nombre de usuario real (todo en minúsculas).
- `IP_DEL_POSTGRES_DOKPLOY`, `tu_user`, `tu_pass`: Las credenciales de tu base de datos actual.
- Credenciales como `SECRETO_RABBIT` o `SECRETO_MINIO123!` por contraseñas seguras.
- Las variables de Auth0 y Gemini (`${GEMINI_API_KEY}`). En Dokploy puedes añadir estas variables directamente en la pestaña **Environment** de tu aplicación Compose.

> **Variables obligatorias (desde la fase 1 de `audit-remediation-2026-10`).** El `docker-compose.yml` del repositorio usa `${VAR:?}`, así que el despliegue falla si falta alguna. Ya no hay valores por defecto.
> - `RABBITMQ_PASS`: contraseña de RabbitMQ.
> - `QDRANT_API_KEY`: API key de Qdrant. La API y los workers no arrancan en Production sin ella.
> - `INTERNAL_GRPC_KEY`: clave compartida entre el scraper Go y el gRPC interno de la API. Por ejemplo, `openssl rand -hex 32`.
> - `CLOUDFLARE_R2_*`: endpoint, credenciales y bucket. La API valida al arrancar que estén definidas.
>
> Además, las APIs de administración exigen el rol `administrador` en Auth0, y los usuarios deben pertenecer a una Organization: sin `org_id` reciben 403.
>
> El ejemplo de esta página es anterior y todavía usa valores por defecto (`CLAVE_QDRANT`, `SECRETO_RABBIT`). Toma como referencia el `docker-compose.yml` del repositorio.

### Despliegue de la nueva topología de RabbitMQ (fase 3 de `audit-remediation-2026-10`)

Las colas `documents_to_process_baseline` y `documents_to_process_hierarchical` pasan a declararse con argumentos de dead-letter, y se añaden las colas `.retry` y `.dlq` (ver `docs/messaging-contract.md`). RabbitMQ no permite cambiar los argumentos de una cola que ya existe: si se despliega sin recrearlas, los servicios fallan con `PRECONDITION_FAILED`. Hay que hacerlo **una sola vez**, en este orden:

1. **Parar la producción de mensajes:** detén el contenedor `go-scraper` y no lances encolados ni reprocesados desde la pantalla de Documentos.
2. **Vaciar las colas:** deja que los workers terminen y comprueba en la consola de RabbitMQ (pestaña *Queues*) que `Ready` y `Unacked` están a 0 en las dos colas. Si hay mensajes que no te importa perder, puedes purgarlas.
3. **Borrar las colas antiguas** (desde la consola, o con `rabbitmqadmin delete queue name=...`):
   - `documents_to_process_baseline`
   - `documents_to_process_hierarchical`
4. **Desplegar a la vez** las nuevas imágenes de `apiservice`, `worker-baseline`, `worker-hierarchical` y `go-scraper`. El primero que arranque declara la topología nueva, y los demás la declaran con los mismos argumentos.
5. **Comprobar** que existen `Q`, `Q.retry` y `Q.dlq` para las dos colas, y lanzar un encolado de prueba desde Documentos.
6. **Vigilar las colas `.dlq`:** contienen los documentos que agotaron los reintentos. Para reintentarlos, usa la acción *Move messages* de la consola hacia la cola original, o vuelve a encolarlos desde Documentos.

Si hay que volver atrás: despliega las imágenes anteriores después de borrar las colas otra vez. Las versiones anteriores las declaran sin argumentos.

```yaml
services:
  qdrant:
    image: qdrant/qdrant:latest
    restart: always
    environment:
      - QDRANT__SERVICE__API_KEY=${QDRANT_API_KEY:-CLAVE_QDRANT}
    volumes:
      - qdrant_data:/qdrant/storage

  rabbitmq:
    image: rabbitmq:3.13-management-alpine
    restart: always
    environment:
      RABBITMQ_DEFAULT_USER: "admin"
      RABBITMQ_DEFAULT_PASS: "SECRETO_RABBIT"
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq

  minio:
    image: minio/minio:latest
    restart: always
    environment:
      MINIO_ROOT_USER: "admin"
      MINIO_ROOT_PASSWORD: "SECRETO_MINIO123!"
    command: server /data --console-address ":9001"
    volumes:
      - minio_data:/data

  minio-setup:
    image: minio/mc:latest
    depends_on:
      - minio
    entrypoint: >
      /bin/sh -c "
      sleep 10;
      mc alias set myminio http://minio:9000 admin SECRETO_MINIO123!;
      mc mb myminio/boletines || true;
      mc anonymous set public myminio/boletines || true;
      "

  apiservice:
    image: ghcr.io/TU_USUARIO_GITHUB/asistente-ayuntamiento-asistenteayuntamiento.apiservice:latest
    restart: always
    environment:
      ConnectionStrings__asistente-ayuntamiento-db: "Host=IP_DEL_POSTGRES_DOKPLOY;Database=ayuntamiento;Username=tu_user;Password=tu_pass"
      ConnectionStrings__qdrant: "Endpoint=http://qdrant:6334;Key=${QDRANT_API_KEY:-CLAVE_QDRANT}"
      ConnectionStrings__messaging: "amqp://admin:SECRETO_RABBIT@rabbitmq:5672"
      Ai__Chat__Provider: "google"
      Ai__Chat__Model: "gemini-1.5-pro"
      Ai__Chat__ApiKey: "${GEMINI_API_KEY}"
      Ai__Embeddings__Provider: "google"
      Ai__Embeddings__Model: "text-embedding-004"
      Ai__Embeddings__ApiKey: "${GEMINI_API_KEY}"
      Blob__Endpoint: "http://minio:9000"
      Blob__AccessKeyId: "admin"
      Blob__SecretAccessKey: "SECRETO_MINIO123!"
      Blob__BucketName: "boletines"
      Auth0__Domain: "${AUTH0_DOMAIN}"
      Auth0__Audience: "${AUTH0_AUDIENCE}"

  worker-baseline:
    image: ghcr.io/TU_USUARIO_GITHUB/asistente-ayuntamiento-asistenteayuntamiento.worker:latest
    restart: always
    environment:
      WORKER_PIPELINE_MODE: "BASELINE"
      ConnectionStrings__asistente-ayuntamiento-db: "Host=IP_DEL_POSTGRES_DOKPLOY;Database=ayuntamiento;Username=tu_user;Password=tu_pass"
      ConnectionStrings__qdrant: "Endpoint=http://qdrant:6334;Key=${QDRANT_API_KEY:-CLAVE_QDRANT}"
      ConnectionStrings__messaging: "amqp://admin:SECRETO_RABBIT@rabbitmq:5672"
      Ai__Chat__Provider: "google"
      Ai__Chat__Model: "gemini-1.5-pro"
      Ai__Chat__ApiKey: "${GEMINI_API_KEY}"
      Ai__Embeddings__Provider: "google"
      Ai__Embeddings__Model: "text-embedding-004"
      Ai__Embeddings__ApiKey: "${GEMINI_API_KEY}"
      Blob__Endpoint: "http://minio:9000"
      Blob__AccessKeyId: "admin"
      Blob__SecretAccessKey: "SECRETO_MINIO123!"
      Blob__BucketName: "boletines"

  worker-hierarchical:
    image: ghcr.io/TU_USUARIO_GITHUB/asistente-ayuntamiento-asistenteayuntamiento.worker:latest
    restart: always
    environment:
      WORKER_PIPELINE_MODE: "HIERARCHICAL"
      ConnectionStrings__asistente-ayuntamiento-db: "Host=IP_DEL_POSTGRES_DOKPLOY;Database=ayuntamiento;Username=tu_user;Password=tu_pass"
      ConnectionStrings__qdrant: "Endpoint=http://qdrant:6334;Key=${QDRANT_API_KEY:-CLAVE_QDRANT}"
      ConnectionStrings__messaging: "amqp://admin:SECRETO_RABBIT@rabbitmq:5672"
      Ai__Chat__Provider: "google"
      Ai__Chat__Model: "gemini-1.5-pro"
      Ai__Chat__ApiKey: "${GEMINI_API_KEY}"
      Ai__Embeddings__Provider: "google"
      Ai__Embeddings__Model: "text-embedding-004"
      Ai__Embeddings__ApiKey: "${GEMINI_API_KEY}"
      Blob__Endpoint: "http://minio:9000"
      Blob__AccessKeyId: "admin"
      Blob__SecretAccessKey: "SECRETO_MINIO123!"
      Blob__BucketName: "boletines"

  go-scraper:
    image: ghcr.io/TU_USUARIO_GITHUB/asistente-ayuntamiento-go-scraper:latest
    restart: always
    environment:
      ConnectionStrings__messaging: "amqp://admin:SECRETO_RABBIT@rabbitmq:5672"
      Blob__Endpoint: "http://minio:9000"
      Blob__AccessKeyId: "admin"
      Blob__SecretAccessKey: "SECRETO_MINIO123!"
      Blob__BucketName: "boletines"
      DOTNET_API_GRPC_URL: "http://apiservice:50051"

  gateway:
    image: ghcr.io/TU_USUARIO_GITHUB/asistente-ayuntamiento-asistenteayuntamiento.gateway:latest
    restart: always
    ports:
      - "8080:8080"
    environment:
      ReverseProxy__Clusters__apiservice-cluster__Destinations__destination1__Address: "http://apiservice:8080"

  webfrontend:
    image: ghcr.io/TU_USUARIO_GITHUB/asistente-ayuntamiento-asistenteayuntamiento.web:latest
    restart: always
    environment:
      services__apiservice__http__0: "http://apiservice:8080"
      Auth0__Domain: "${AUTH0_DOMAIN}"
      Auth0__ClientId: "${AUTH0_CLIENT_ID}"
      Auth0__ClientSecret: "${AUTH0_CLIENT_SECRET}"
      Blob__Endpoint: "http://minio:9000"
      Blob__AccessKeyId: "admin"
      Blob__SecretAccessKey: "SECRETO_MINIO123!"
      Blob__BucketName: "boletines"

volumes:
  rabbitmq_data:
  minio_data:
```
