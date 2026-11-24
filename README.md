# Asistente Ayuntamiento

AI assistant for the staff of local public administrations. It downloads the Spanish official bulletins every day (BOE
and BOJA), indexes them, and answers questions in Spanish with retrieval-augmented generation (RAG), citing the official
documents each answer is based on.

- **Users:** civil servants and administrative staff of municipalities. Each municipality or public body is a tenant
  (an Auth0 Organization) with its users, settings and history kept apart.
- **Answers:** grounded only in the retrieved bulletins, with the cited sources appended and an abstention when the
  documents do not answer the question. The UI states that answers are AI-generated and must be checked at the source.
- **Models:** configurable per deployment: open-weight models on the same server with Ollama, or a provider
  (Google, OpenAI-compatible, OpenRouter). Cost and usage are recorded per call.

## Architecture

| Component | Technology | Role |
|---|---|---|
| Scraper | Go | Downloads BOE and BOJA daily, stores the documents in S3-compatible storage, queues them |
| Worker | .NET 10, Semantic Kernel | Chunks and embeds documents (baseline and hierarchical pipelines) |
| ApiService | ASP.NET Core, SignalR, gRPC | Chat, REST API, tenants, metrics, evaluation results |
| Gateway | ASP.NET Core, YARP | Serves the Angular UI and proxies the API |
| Data | PostgreSQL, Qdrant, RabbitMQ, S3 (R2, MinIO, RustFS) | Documents and metadata, vectors, queues, files |
| Observability | OpenTelemetry, Grafana | Traces, metrics, logs and browser telemetry |

More: [docs/architecture.md](docs/architecture.md), [docs/README.md](docs/README.md).

## API

- `POST /api/v1/query`: answers a question with the same pipeline as the chat, without storing history (bearer token
  of a user in an organization).
- The chat UI streams over SignalR at `/hubs/chat`.
- OpenAPI document: `/openapi/v1.json` (disable with `OpenApi:Enabled=false`), versioned in
  [docs/api/openapi.v1.json](docs/api/openapi.v1.json). A test fails when they differ; regenerate the file with
  `UPDATE_OPENAPI=1 dotnet test tests/AsistenteAyuntamiento.ApiService.Tests --filter OpenApiDocumentTests`.
- Probes: `/health` (all checks) and `/alive` (liveness) on every service, in every environment. They return only the
  status word.

## Run it

- **Development:** .NET Aspire starts every service and its dependencies: see [docs/setup.md](docs/setup.md).
- **Deployment:** Dokploy, standalone Docker Compose or Kubernetes: see [deploy/README.md](deploy/README.md).
- **Evaluation:** offline RAG evaluation (RAGAS) of the pipelines: see [eval/README.md](eval/README.md).

## Licence

Copyright © 2026 Antonio Bermúdez. All rights reserved. The licence will be decided before the code is published.
