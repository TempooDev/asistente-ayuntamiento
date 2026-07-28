# Design: Arquitectura del Scraper del BOE

## Estructura del Proyecto
* **Lenguaje:** Go (ideal por su alto rendimiento y manejo de concurrencia).
* **Entorno Local:** `air` para *hot reload* y .NET Aspire AppHost para orquestación.

## Componentes Principales
1. **Core / Interfaces (`pkg/scraper`)**: 
   * Interfaz `BoletinProvider` con métodos estándar como `FetchSummary(date time.Time) ([]string, error)` y `FetchDocument(id string) (*Document, error)`.
   * Estructura base de `Document` e inyección de metadatos en *chunks*.
2. **Implementación BOE (`pkg/boe`)**:
   * Cliente específico que consume los endpoints web diarios (`https://www.boe.es/diario_boe/xml.php?id=BOE-S-YYYYMMDD` y `https://www.boe.es/diario_boe/xml.php?id={ID}`) para garantizar la inmediatez de los datos, descartando el portal de Datos Abiertos por el posible retraso en la sincronización.
3. **Delegación de Chunking**:
   * El servicio de Go no realiza el chunking. Se encarga únicamente de formar el documento JSON (`metadata` + texto plano) y guardarlo, delegando la fase de chunking semántico y embeddings al orquestador .NET usando Semantic Kernel.
4. **Almacenamiento (`pkg/storage`)**:
   * Interfaz y adaptadores para guardar el resultado estructurado JSON en Blob Storage.

## Flujo de Trabajo
* Ejecución programada o disparada manualmente.
* El orquestador invoca la obtención del sumario, extrae los IDs, y descarga cada documento de forma concurrente usando *Goroutines* y *Channels*, controlados por un *rate limiter* (ej. `golang.org/x/time/rate`).
* Se parsea el XML, se generan los *chunks* JSON, y se guardan en Blob Storage, manejando errores (Dead Letter Queue) y reintentos (Exponential Backoff).
