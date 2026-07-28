# Tasks

- [x] **Tarea 1:** Inicializar el proyecto Go (ej. en `src/go-scraper`), crear el `go.mod` y configurar el archivo `.air.toml` para el hot-reload.
- [x] **Tarea 2:** Integrar el proyecto de Go en el orquestador .NET Aspire (`AppHost`), añadiendo el ejecutable/servicio para que arranque automáticamente al hacer F5.
- [x] **Tarea 3:** Definir el modelo de datos base en Go (JSON output) y la interfaz `BoletinProvider` que facilitará la extensibilidad (BOJA, BOPMA).
- [x] **Tarea 4:** Implementar el cliente del BOE: llamadas a la API de sumario y parseo de XML individuales con manejo de concurrencia, limitadores de tasa y reintentos.
- [x] **Tarea 5:** (Eliminada/Modificada) El chunking semántico se delega a .NET con Semantic Kernel en un proceso batch y se guardará en PostgreSQL (pgvector). El modelo en Go solo formará el JSON base con el texto completo.
- [x] **Tarea 6:** Implementar el módulo de Storage para guardar los XML descargados (como backup) y los JSON vectorizables en el Blob Storage.
- [x] **Tarea 7:** Configurar logs estructurados y realizar pruebas unitarias o de integración validando el volcado correcto de un sumario completo de prueba.
