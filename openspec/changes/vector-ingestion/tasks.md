# Tasks: Vector Ingestion and RabbitMQ Consumer

- [x] **Tarea 1:** Configurar PostgreSQL con la extensión `pgvector` e instalar/configurar `Npgsql.EntityFrameworkCore.PostgreSQL` en el proyecto `.NET (ApiService)`.
- [x] **Tarea 2:** Crear la entidad `DocumentChunk` y su configuración en el `DbContext`, para incluir el campo `Vector` mapeado a `vector(N)` (dimensión dependiente del modelo a usar).
- [x] **Tarea 3:** Asegurar que `AppHost.cs` proporcione un modelo adecuado de embeddings en Ollama (por ejemplo, añadiendo `nomic-embed-text` a la configuración de `builder.AddOllama`) e inicializar la conexión de Semantic Kernel en .NET para consumir estos embeddings.
- [x] **Tarea 4:** Implementar la lógica del `RabbitMqConsumerService` heredando de `BackgroundService`, para suscribirse y parsear el `DocumentMessage` de forma asíncrona y transaccional.
- [x] **Tarea 5:** Integrar la descarga de blobs desde Azurite / Azure Blob Storage dentro del flujo del consumidor, convirtiendo el JSON descargado en el modelo nativo de C#.
- [x] **Tarea 6:** Implementar el particionado de texto utilizando `Microsoft.SemanticKernel.Text` e invocar la API de embeddings de Ollama, volcando los resultados finales a la tabla de Postgres y confirmando (ACK) el mensaje de RabbitMQ.
