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
6. Haz clic en **Deploy**. Dokploy descargará las imágenes y levantará toda la arquitectura (Qdrant, RabbitMQ, API, workers, pasarela con la web y scraper). Los documentos se guardan en Cloudflare R2: en producción no hay almacenamiento S3 local.

### Workers del estudio de chunking (perfil `chunking-study`)

`worker-recursive` y `worker-semantic` ingieren las variantes recursiva y semántica del estudio de chunking (`eval/README.md`). Usan la misma imagen que los otros workers y solo arrancan con el perfil de Compose `chunking-study`, así que un despliegue normal levanta únicamente `worker-baseline` y `worker-hierarchical`:

```bash
docker compose --profile chunking-study up -d worker-recursive worker-semantic
```

En Dokploy, añade `COMPOSE_PROFILES=chunking-study` en la pestaña **Environment** para activarlos. Sin ellos, los documentos que un administrador encole para estas variantes esperan en `documents_to_process_recursive` y `documents_to_process_semantic`. Ajustes opcionales (por defecto, los de `AI_EMBEDDINGS_*`): `AI_CHUNKING_RECURSIVE_CHUNK_MAX_TOKENS`, `AI_CHUNKING_RECURSIVE_CHUNK_OVERLAP_TOKENS`, `AI_CHUNKING_SEMANTIC_BUFFER_SIZE` (1), `AI_CHUNKING_SEMANTIC_BREAKPOINT_PERCENTILE` (95) y `AI_CHUNKING_SEMANTIC_CHUNK_MAX_TOKENS`. El tamaño mínimo de chunk semántico no se fija en el compose: el Worker usa `min(100, ChunkMaxTokens / 4)`, de modo que un `ChunkMaxTokens` pequeño sigue siendo válido. La variante semántica embebe cada frase del documento con el modelo de embeddings de la plataforma, así que con un proveedor de pago la ingesta cuesta varias veces lo que la baseline.

## Paso 4: Versiones de las imágenes y vuelta atrás (rollback)

El `docker-compose.yml` del repositorio usa `${IMAGE_TAG:-latest}` en todas las imágenes de la aplicación (`apiservice`, `worker-baseline`, `worker-hierarchical`, `worker-recursive`, `worker-semantic`, `go-scraper` y `gateway`). Sin `IMAGE_TAG`, o con `IMAGE_TAG=latest`, se despliega la última versión publicada.

Cada despliegue de `.github/workflows/deploy.yml` publica la etiqueta `sha-<7 primeros caracteres del commit>`:

- Las imágenes que cambian en ese commit se compilan con `sha-xxxxxxx` y `latest`.
- Las que no cambian no se recompilan: el job `trigger-deployment` copia su `latest` actual a `sha-xxxxxxx` con `docker buildx imagetools create`, sin descargar ni recompilar nada. Así, cada `sha-xxxxxxx` desplegado existe para **las cuatro imágenes** y corresponde exactamente a lo que estaba en producción tras ese despliegue.

**Para volver a una versión anterior:**

1. Busca el commit al que quieres volver: el run de *Build and Push Docker Images* muestra `Triggering deployment of sha-xxxxxxx`, o consulta las etiquetas en GitHub > **Packages**.
2. En Dokploy, en la pestaña **Environment** de la aplicación Compose, pon `IMAGE_TAG=sha-xxxxxxx`.
3. Pulsa **Deploy** (o **Redeploy**). Con `pull_policy: always` se descargan exactamente esas imágenes.
4. Comprueba que los servicios arrancan y que la incidencia ha desaparecido.
5. Cuando el arreglo llegue a `main`, vuelve a dejar `IMAGE_TAG=latest` (o bórrala). **Mientras `IMAGE_TAG` apunte a un `sha-*`, los despliegues automáticos siguen levantando esa versión fija**, aunque el workflow publique imágenes nuevas.

Limitaciones:

- Los despliegues anteriores a este cambio solo tienen `sha-xxxxxxx` en las imágenes que se compilaron en ese commit. Para volver a uno de ellos, comprueba en **Packages** que la etiqueta existe en las cuatro imágenes; si falta en alguna, no uses ese `sha`.
- La API aplica las migraciones de EF Core al arrancar (`Database:MigrateOnStartup`) y una versión anterior **no deshace** las migraciones nuevas. Si el despliegue que quieres revertir incluía una migración, revisa que el esquema siga siendo compatible con la versión anterior antes de hacer rollback.
- Los cambios de topología de RabbitMQ siguen necesitando los pasos manuales de su sección.

---

## Variables del `docker-compose.yml`

Usa el `docker-compose.yml` de la raíz del repositorio. Las variables se definen en la pestaña **Environment** de la aplicación Compose en Dokploy; la lista completa, con sus valores por defecto, está en `.env.example`.

> **Variables obligatorias (desde la fase 1 de `audit-remediation-2026-10`).** El `docker-compose.yml` del repositorio usa `${VAR:?}`, así que el despliegue falla si falta alguna. Ya no hay valores por defecto.
> - `RABBITMQ_PASS`: contraseña de RabbitMQ.
> - `QDRANT_API_KEY`: API key de Qdrant. La API y los workers no arrancan en Production sin ella.
> - `INTERNAL_GRPC_KEY`: clave compartida entre el scraper Go y el gRPC interno de la API. Por ejemplo, `openssl rand -hex 32`.
> - `CLOUDFLARE_R2_*`: endpoint, credenciales y bucket. La API valida al arrancar que estén definidas.
> Además, las APIs de administración exigen los roles `admin-plataforma` (ingesta, scraper) o `admin-organizacion` (IA y métricas del tenant) en Auth0, y los usuarios deben pertenecer a una Organization: sin `org_id` reciben 403.

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

El ejemplo de `docker-compose.yml` que había aquí se ha eliminado porque estaba desfasado (MinIO, valores por defecto). La referencia es el `docker-compose.yml` de la raíz del repositorio; para un despliegue sin Dokploy, ver `deploy/README.md`.
