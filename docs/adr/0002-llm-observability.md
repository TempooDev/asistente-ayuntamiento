# ADR 0002: Observabilidad específica de LLM (spike de Arize Phoenix)

## Fecha
2026-10-13

## Estado
Aceptado. Se cierra el spike con **«Grafana basta por ahora»**. Phoenix queda documentado como opción para cuando se pueda capturar contenido.

## Contexto
El cambio `observability-grafana-tempooanalytics` define dos niveles de observabilidad del LLM:

- **Nivel 1, ya implementado:**
  - métricas GenAI de OpenTelemetry en Mimir: tokens, duración, tiempo hasta el primer token, coste en EUR, tenant, pipeline y respaldo;
  - trazas en Tempo, con spans de Semantic Kernel sin contenido;
  - el dashboard «LLM» de Grafana.
- **Nivel 2, opcional:** una herramienta propia de LLM (Phoenix o Langfuse) que reciba los mismos datos por un segundo exportador del Collector. La tarea 6.1 pide un spike acotado con Phoenix primero.

El contenido de las preguntas y respuestas **no** se envía a la telemetría. Las preguntas pueden contener datos personales, y activarlo exige una revisión de privacidad y un cambio aparte (tarea 6.4). `TelemetryPrivacyTests` y el filtro de atributos del Collector lo garantizan.

## Spike (13/10/2026)

**Montaje, todo en local con Podman:**
- `arizephoenix/phoenix:version-20.21.1`.
- `otel/opentelemetry-collector-contrib:0.162.0`, con dos exportadores de trazas (Phoenix y fichero) y el procesador `attributes/drop-content`, como en `docs/observability-architecture.md`.
- Un arnés .NET que ejecuta el **código real** del proyecto:
  - `ClearLanguageGenerationService` y Semantic Kernel 1.80 con el switch de diagnósticos sin contenido;
  - las mismas fuentes que `ServiceDefaults`;
  - un servidor falso compatible con OpenAI, que devuelve el uso de tokens igual que OpenAI, también en streaming.
- Escenarios: una respuesta jerárquica en streaming, una batalla de Arena con los dos lados bajo la misma petición, y una llamada que falla con 429.

**Resultados:**

| Aspecto | Resultado |
|---|---|
| Integración | Sin cambios de código. Phoenix recibe OTLP del Collector y traduce `gen_ai.*` a OpenInference (`llm.token_count.*`, spans de tipo `LLM`). |
| Contenido | Ningún span lleva la pregunta (ni el DNI de prueba) ni la respuesta. Correcto, pero deja a Phoenix sin lo que más aporta. |
| Tokens | Aparecen en nuestros spans (`AI.ChatCompletion*`). Los spans de Semantic Kernel en streaming no traen el uso; es una limitación de SK. |
| Coste | Phoenix calcula su propio coste en USD con su tabla de modelos. El nuestro (EUR, `Ai:Pricing`) ya está en Grafana. |
| Árbol de la petición | Petición → pipeline → llamada al modelo, con los dos lados de Arena juntos. Tempo ofrece la misma vista. |
| Votos humanos | Existe la API de anotaciones (`POST /v1/span_annotations`). Los votos de Arena se podrían enviar como anotación del trace guardado en `ArenaBattle.TraceId`. |
| Recursos | Phoenix ~460 MB de RAM en reposo, imagen de 1 GB (SQLite dentro del contenedor; Postgres para persistencia seria). Collector ~35 MB. |

**Fallo encontrado y corregido.** El spike destapó que `TokenUsageExtractor` no leía el uso de OpenAI ni de OpenRouter (`ChatTokenUsage.InputTokenCount` / `OutputTokenCount`), ni la salida de Gemini (`CandidatesTokenCount`). Con esos proveedores, los tokens y el coste del nivel 1 se registraban a 0, también en `AiCallLogs` y en las analíticas. Ahora tiene tests por proveedor.

## Decisión
1. **No se despliega Phoenix por ahora (6.3).** Sin contenido, su valor diferencial frente a Grafana es pequeño:
   - una vista de trazas centrada en LLM;
   - el coste en USD;
   - anotaciones.

   Lo que justificaría una herramienta propia (revisar preguntas y respuestas, playground, datasets y evaluaciones) depende del contenido, que no se captura. A cambio habría que mantener otro servicio en el homelab (~0,5 GB de RAM más su almacenamiento).
2. **No se hace el spike de Langfuse (6.2).** La condición era que Phoenix mostrara carencias en versionado de prompts o puntuaciones humanas. Phoenix tiene ambas cosas, y sin contenido ninguna de las dos herramientas aporta más. Langfuse además necesita Postgres, ClickHouse, Redis y S3.
3. **La captura de contenido sigue desactivada (6.4)** en todos los entornos.

## Cuándo revisar esta decisión
- Si una revisión de privacidad aprueba capturar contenido, al menos de forma anonimizada y con retención corta.
- Si se quieren evaluaciones automáticas o datasets a partir de conversaciones reales.

En ese caso, Phoenix es la primera opción porque es ligero y nativo de OTLP:
- se añade como exportador `otlp/llm-tool` en el Collector (el hueco ya está en `docs/observability-architecture.md`);
- los votos de Arena se envían como anotaciones;
- los servicios no cambian.

## Cómo reproducir el spike
```bash
podman network create llmspike
podman run -d --name phoenix --network llmspike -p 6006:6006 -p 24317:4317 docker.io/arizephoenix/phoenix:version-20.21.1
podman run -d --name collector --network llmspike -p 14317:4317 \
  -v ./collector.yaml:/etc/otelcol-contrib/config.yaml:ro docker.io/otel/opentelemetry-collector-contrib:0.162.0
```

En `collector.yaml`, el exportador `otlp/phoenix` apunta a `host.containers.internal:24317`. Con Podman, la resolución por nombre de contenedor no funcionó dentro de la red. Después:
1. Arranca la API con `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:14317`.
2. Abre `http://localhost:6006`.
