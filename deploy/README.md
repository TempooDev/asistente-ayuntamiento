# Deployment options

Three ways to run the assistant. All use the same images, variables and services; pick the one that fits the host.

| Option | Files | When |
|---|---|---|
| Dokploy (current production) | `docker-compose.yml` at the repository root, `docs/deploy-dokploy.md` | A VPS with Dokploy, images from GHCR, PostgreSQL managed by Dokploy, documents in Cloudflare R2 |
| Standalone Docker Compose | `deploy/compose/` | One server (a municipal server or an EU cloud VM) with only Docker: everything runs on it, images are built from source |
| Kubernetes | `deploy/k8s/` | A cluster; object storage is external (R2, S3, MinIO) |

## Standalone Docker Compose

Requirements: Docker with Compose v2, 4 vCPU and 16 GB RAM for the services and small local models (more, or a GPU, for
larger chat models), outbound HTTPS to `boe.es` and `juntadeandalucia.es` for the daily bulletins, and an Auth0 tenant.

```bash
cp deploy/compose/.env.example deploy/compose/.env    # fill in the required values
docker compose -f deploy/compose/docker-compose.standalone.yml --env-file deploy/compose/.env up -d --build
```

- The UI and the API are served on `http://<host>:${GATEWAY_PUBLISHED_PORT}` (8080 by default). Put a TLS reverse
  proxy in front of it.
- `COMPOSE_PROFILES=local-models` runs Ollama in the compose and pulls the configured models: no query leaves the server.
  Without it, set `AI_CHAT_PROVIDER`/`AI_EMBEDDINGS_PROVIDER` (`google`, `openai`, `openrouter`) and their keys, or point
  `OLLAMA_ENDPOINT` to an Ollama server on the private network.
- The ApiService applies the database migrations and creates the bucket on startup.
- `CHAT_RETENTION_DAYS` deletes inactive chat history after that many days (0 keeps it).

## Kubernetes

```bash
cp deploy/k8s/secret.example.yaml secret.yaml        # fill in, never commit it
kubectl create namespace asistente-ayuntamiento
kubectl apply -n asistente-ayuntamiento -f secret.yaml
kubectl apply -k deploy/k8s
```

Edit `configmap.yaml` (models, bulletins, retention), the Ingress host and the image tags in `kustomization.yaml`.
The manifests mirror the compose files: single replicas, StatefulSets with volumes for PostgreSQL, Qdrant and
RabbitMQ, and external object storage. They have not been run against a cluster yet.

## Building the images

Each .NET service has a Dockerfile (build context = repository root); the scraper builds from `src/go-scraper`:

```bash
docker build -f src/AsistenteAyuntamiento.ApiService/Dockerfile -t asistente/apiservice .
docker build -f src/AsistenteAyuntamiento.Worker/Dockerfile -t asistente/worker .
docker build -f src/AsistenteAyuntamiento.Gateway/Dockerfile -t asistente/gateway .   # includes the Angular UI
docker build -t asistente/go-scraper src/go-scraper
```

CI publishes the same services to GHCR with `dotnet publish -t:PublishContainer` (`.github/workflows/deploy.yml`).
