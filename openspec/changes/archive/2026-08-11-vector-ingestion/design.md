# Design: Vector Ingestion and RabbitMQ Consumer

## Architecture

1. **Mensajería (RabbitMQ)**
   - El contenedor RabbitMQ inyectado por Aspire expone la cola `documents_to_process`.
   - Se creará un `BackgroundService` en el proyecto de .NET (`AsistenteAyuntamiento.ApiService`) que utilizará `RabbitMQ.Client` para consumir mensajes de forma concurrente, asegurando de usar `Acknowledge` solo al completar la vectorización con éxito.

2. **Acceso al Almacenamiento (Azure Blob Storage / Azurite)**
   - Se utilizará `Azure.Storage.Blobs` desde .NET.
   - Usando el `BlobPath` incluido en el mensaje (ej. `json/BOE/BOE-A-2026-1234.json`), el consumidor descargará el documento y lo deserializará en un modelo C# equivalente a la estructura generada por el Scraper en Go.

3. **Semantic Kernel & Chunking**
   - El texto extraído (contenido del boletín) suele ser extenso. Se utilizará Semantic Kernel (`Microsoft.SemanticKernel.Text`) para dividir el texto en chunks manejables (ej. 1000 tokens) cuidando de no romper párrafos y manteniendo solapamiento (overlap).
   
4. **Vectorización (Embeddings)**
   - Se utilizará un modelo de embeddings de Ollama (ej. `nomic-embed-text` o un modelo configurado de `llama3.2`). 
   - El contenedor de Ollama provisto por Aspire servirá como host para la generación de vectores.

5. **Persistencia (PostgreSQL + pgvector)**
   - Utilizaremos Entity Framework Core (`Npgsql.EntityFrameworkCore.PostgreSQL` + el plugin para `pgvector`).
   - Se creará una tabla/entidad `DocumentChunk` que almacenará el metadata base (DocumentID, Source, Título, etc.), el texto del chunk y un `Vector` (arreglo de flotantes de dimensión N).

## Data Models

**DocumentMessage (consumido de RabbitMQ)**
```csharp
public class DocumentMessage
{
    public string Source { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
}
```

**DocumentChunk (Entidad DB con pgvector)**
```csharp
public class DocumentChunk
{
    public int Id { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty; // Chunk text
    public int ChunkIndex { get; set; }
    public DateTime PublicationDate { get; set; }
    
    // Configurado en EF Core: .HasColumnType("vector(N)")
    public float[] Embedding { get; set; } = Array.Empty<float>();
}
```

## Security & Reliability
- **Retry Policy / NACK**: Si la descarga del blob o la generación del embedding falla (ej. por timeout de Ollama), el consumidor debe rechazar (NACK) el mensaje y devolverlo a la cola, apoyándose en una política de retries limitados.
- **Transaccionalidad**: La inserción de todos los chunks de un mismo documento debe encapsularse en una transacción de base de datos para no dejar documentos parcialmente ingeridos ante un cuelgue de red.
