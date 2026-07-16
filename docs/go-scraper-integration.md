# Go Scraper & Ingestion Integration Design

This document details the architecture, design, and data contracts for integrating the Go-based Scraper service into the DemoTfg RAG system, leveraging Cosmtrek Air for hot-reloading in the .NET Aspire developer environment.

---

## 1. System Architecture Diagram

The Go Scraper runs as an independent worker alongside the .NET core application, orchestrated by .NET Aspire.

```mermaid
graph TD
    %% Scraper Components
    Sources[Official Gazettes: BOE, BOJA, BOPM] -->|Scrape| GoScraper[Go Scraper Service]
    GoScraper -->|Upload PDF| Storage[Azure Storage / S3]
    GoScraper -->|Publish Ingest Event| RabbitMQ[RabbitMQ Message Broker]

    %% Ingestion / Processing Components
    RabbitMQ -->|Consume Event| dotnetWorker[C# Semantic Kernel Worker]
    Storage -->|Read PDF| dotnetWorker
    dotnetWorker -->|Request Embeddings / LLM| Ollama[Ollama Server]
    dotnetWorker -->|Store Vectors| Postgres[(PostgreSQL + pgvector)]
    
    %% API and Frontend
    Web[Blazor Web Frontend] -->|HTTP / API| API[API Service]
    API -->|Read/Write Data| Postgres
```

---

## 2. Go Scraper Service Overview

The scraper is written in Go to take advantage of its superior concurrency primitives, low memory footprint, and rapid startup.

- **Web Framework:** [Echo](https://echo.labstack.com/) for exposing `/health` endpoints and triggerable manual endpoints.
- **Hot Reload (Dev):** [Air](https://github.com/cosmtrek/air) watches files, recompiles, and restarts the service inside the .NET Aspire environment.
- **Scraping Libraries:** Pure HTTP clients, `goquery`, or `colly` for parsing gazette index pages.

---

## 3. RabbitMQ Ingestion Event Contract

When the Go Scraper successfully downloads a document from the official gazette and uploads it to the raw storage bucket/container, it publishes an ingestion event to RabbitMQ.

### Queue/Exchange Details
- **Exchange:** `gazette-ingestion-exchange` (Topic or Direct)
- **Routing Key / Queue Name:** `gazette-ingest-queue`

### Message JSON Schema

```json
{
  "eventId": "a5e8c4b9-8e4d-4b8f-8c3d-1a2b3c4d5e6f",
  "source": "BOE",
  "documentId": "BOE-A-2026-12345",
  "title": "Resolución de 12 de julio de 2026, de la Dirección General...",
  "originalUrl": "https://www.boe.es/diario_boe/xml.php?id=BOE-A-2026-12345",
  "storagePath": "raw-documents/boe/2026/07/15/BOE-A-2026-12345.pdf",
  "publishedAt": "2026-07-12T00:00:00Z",
  "scrapedAt": "2026-07-15T14:58:00Z",
  "metadata": {
    "section": "Sección I: Disposiciones generales",
    "department": "Ministerio de Hacienda",
    "contentType": "application/pdf",
    "fileSize": 1048576
  }
}
```

#### Fields Description
| Field | Type | Description |
| :--- | :--- | :--- |
| `eventId` | `string` | Unique identifier (UUID v4) for tracing this ingestion attempt. |
| `source` | `string` | The publication source: `BOE`, `BOJA`, or `BOPM`. |
| `documentId` | `string` | Unique identifier assigned by the official publisher. |
| `title` | `string` | Headline or brief summary title of the resolution/document. |
| `originalUrl` | `string` | Direct link to the source website for tracing back or rendering. |
| `storagePath` | `string` | Relative path or key within the S3/Azure Blob Storage container. |
| `publishedAt` | `string` | Date/time when the gazette was officially published (ISO 8601). |
| `scrapedAt` | `string` | Date/time when the Go worker processed the file (ISO 8601). |
| `metadata` | `object` | Key-value pairs for extendable filtering (department, size, etc.). |

---

## 4. Aspire Integration & Environment Configuration

In the `.NET Aspire AppHost`, the Go resource is registered as a local executable. 

### Development Mode Environment Variables

Aspire automatically injects connection strings as environment variables for references defined using `.WithReference(...)`:

- **RabbitMQ Connection:**
  - Env Var: `ConnectionStrings__messaging`
  - Value: `amqp://guest:guest@localhost:5672/` (or dynamic Aspire dev port)
- **Blob Storage Connection:**
  - Env Var: `ConnectionStrings__storage`
  - Value: Connection string to local Azurite instance.

### Development Launch Config (`AppHost.cs`)

```csharp
// AppHost.cs registration logic
var goScraperPath = Path.Combine("..", "DemoTfg.Scraper");
var scraperCommand = builder.Environment.IsDevelopment() ? "air" : "go";
var scraperArgs = builder.Environment.IsDevelopment() ? Array.Empty<string>() : new[] { "run", "main.go" };

var goScraper = builder.AddExecutable("go-scraper", scraperCommand, goScraperPath, scraperArgs)
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WithHttpHealthCheck("/health")
    .WithReference(messaging)
    .WithReference(storage);
```
