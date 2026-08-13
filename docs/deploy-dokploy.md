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

```yaml
services:
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
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: "admin"
      MINIO_ROOT_PASSWORD: "SECRETO_MINIO123!"
    volumes:
      - minio_data:/data

  minio-init: 
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

  worker:
    image: ghcr.io/TU_USUARIO_GITHUB/asistente-ayuntamiento-asistenteayuntamiento.worker:latest
    restart: always
    environment:
      ConnectionStrings__asistente-ayuntamiento-db: "Host=IP_DEL_POSTGRES_DOKPLOY;Database=ayuntamiento;Username=tu_user;Password=tu_pass"
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

  gateway:
    image: ghcr.io/TU_USUARIO_GITHUB/asistente-ayuntamiento-asistenteayuntamiento.gateway:latest
    restart: always
    ports:
      - "8080:8080"
    environment:
      services__apiservice__http__0: "http://apiservice:8080"
      services__webfrontend__http__0: "http://webfrontend:8080"

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
