# Tasks

- [ ] **Tarea 1:** Inicializar el proyecto Go (ej. en `src/go-scraper`), crear el `go.mod` y configurar el archivo `.air.toml` para el hot-reload.
- [ ] **Tarea 2:** Integrar el proyecto de Go en el orquestador .NET Aspire (`AppHost`), añadiendo el ejecutable/servicio para que arranque automáticamente al hacer F5.
- [ ] **Tarea 3:** Definir el modelo de datos base en Go (JSON output) y la interfaz `BoletinProvider` que facilitará la extensibilidad (BOJA, BOPMA).
- [ ] **Tarea 4:** Implementar el cliente del BOE: llamadas a la API de sumario y parseo de XML individuales con manejo de concurrencia, limitadores de tasa y reintentos.
- [ ] **Tarea 5:** Construir el motor de *chunking* para dividir lógicamente el `<texto>` de los boletines inyectando los metadatos asociados.
- [ ] **Tarea 6:** Implementar el módulo de Storage para guardar los XML descargados (como backup) y los JSON vectorizables en el Blob Storage.
- [ ] **Tarea 7:** Configurar logs estructurados y realizar pruebas unitarias o de integración validando el volcado correcto de un sumario completo de prueba.
