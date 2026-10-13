# Contrato de mensajería (RabbitMQ)

Fuente de verdad del contrato entre el scraper Go y los servicios .NET. Cualquier cambio de nombres, argumentos o formato debe aplicarse a la vez en:

- **.NET:** `src/AsistenteAyuntamiento.Infrastructure/Messaging/RabbitMqTopology.cs`
- **Go:** `src/go-scraper/internal/messaging/rabbitmq.go`

## Colas de trabajo (ingesta)

| Cola | Publica | Consume |
|---|---|---|
| `documents_to_process_baseline` | Scraper Go, `IngestionAdminService` (API) | Worker en modo `BASELINE` (`BaselineIngestionConsumer`) |
| `documents_to_process_hierarchical` | Scraper Go, `IngestionAdminService` (API) | Worker en modo `HIERARCHICAL` (`HierarchicalIngestionConsumer`) |

Cada cola de trabajo `Q` tiene dos colas auxiliares, todas `durable`:

| Cola | Argumentos | Para qué |
|---|---|---|
| `Q` | `x-dead-letter-exchange = ""`, `x-dead-letter-routing-key = Q.dlq` | Trabajo pendiente. Un `nack` sin `requeue` lo manda a `Q.dlq`. |
| `Q.retry` | `x-message-ttl = 30000` (**int32**), `x-dead-letter-exchange = ""`, `x-dead-letter-routing-key = Q` | Espera de 30 s antes de reintentar; al caducar vuelve a `Q`. |
| `Q.dlq` | — | Mensajes que agotaron los reintentos o no se pudieron leer. Se inspeccionan a mano. |

> **Importante:** RabbitMQ no permite cambiar los argumentos de una cola existente (`PRECONDITION_FAILED`). Hay que declararlos exactamente igual en Go y en .NET, incluido el tipo entero (`int32`). Para cambiarlos hay que borrar y recrear las colas (ver `docs/deploy-dokploy.md`).

### Mensaje

`content_type: application/json`, `delivery_mode: 2` (persistente) y `message_id` = `document_id`.

```json
{ "source": "BOE", "document_id": "BOE-A-2026-1234", "blob_path": "json/BOE/BOE-A-2026-1234.json", "pipelines": ["BASELINE", "HIERARCHICAL"] }
```

`pipelines` (opcional) indica a qué colas se ha publicado el documento. El primer worker que lo recibe marca como `Queued` los pipelines de la lista que aún no tienen estado, para que el resumen de la página de Documentos no lo dé por completado mientras el pipeline más lento no lo ha procesado. Los mensajes sin `pipelines` (antiguos o reencolados por un administrador, que registra sus propios estados) no cambian nada.

El blob `json/{SOURCE}/{document_id}.json` lo escribe el scraper:

```json
{
  "document_id": "BOE-A-2026-1234",
  "metadata": {
    "source": "BOE",
    "document_id": "BOE-A-2026-1234",
    "titulo": "...",
    "departamento": "...",
    "fecha_publicacion": "20260915"
  },
  "text": "..."
}
```

- **`fecha_publicacion`:** el BOE la escribe como `yyyyMMdd` y BOJA como `yyyy-MM-dd`.
- **BOE:** guarda además el XML original en `raw-xml/BOE/{document_id}.xml`, que la ingesta jerárquica usa para obtener los artículos.

### Cabeceras

| Cabecera | Tipo | Significado |
|---|---|---|
| `x-retry-count` | int | Reintentos ya hechos. La añade el consumidor al reenviar a `Q.retry`. |
| `traceparent` / `tracestate` | string | Contexto W3C de quien publica (API, consumidor al reintentar o scraper Go). El consumidor continúa esa traza (`RabbitMqTraceContext`). |

### Garantías

- **Publicación:**
  - Los mensajes se publican como persistentes y con *publisher confirms*: quien publica solo da el envío por bueno cuando el broker lo confirma.
  - La API guarda primero el estado `Queued` y después publica. Si la publicación falla, devuelve el documento a `Pending`.
- **Consumo:**
  - `ack` cuando el mensaje se procesa bien.
  - Si falla, se reenvía a `Q.retry` con `x-retry-count + 1`, hasta 3 veces.
  - Agotados los reintentos, `nack` sin `requeue`: el mensaje va a `Q.dlq` y el documento queda `Failed`.
  - Un JSON inválido o un error no recuperable (`NonRetryableMessageException`, por ejemplo una fuente sin procesador) va directo a `Q.dlq`.
- **Idempotencia:** procesar dos veces el mismo `document_id` reemplaza la versión anterior en Postgres y en Qdrant.

## Notificaciones de estado

| Elemento | Valor |
|---|---|
| Exchange | `document_notifications_exchange` (`fanout`, `durable`) |
| Publica | Worker (`RabbitMqNotificationPublisher`) |
| Consume | Cada instancia de la API, con su propia cola `api_notifications_{guid}` (`exclusive`, `auto-delete`) |
| Mensaje | `{ "DocumentId": "...", "NewStatus": "Processing" }` |

Los valores de `NewStatus` son los de `JobStatus`: `Pending`, `Queued`, `Processing`, `Completed`, `Failed` y `VectorSyncFailed`.

Las notificaciones son efímeras: si fallan, se registran en el log y se descartan. No hay reintentos ni DLQ.
