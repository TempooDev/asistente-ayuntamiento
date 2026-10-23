# Evaluación offline de las variantes RAG

Compara las variantes de chunking con las mismas preguntas, el mismo corpus y los mismos modelos (cambio OpenSpec `chunking-study-ragas`, spec `rag-evaluation`).

| Carpeta | Qué contiene |
|---|---|
| `dataset/` | `questions.jsonl` (pregunta, respuesta de referencia, documentos que la responden y categoría) y `corpus.txt` (blobs `json/{FUENTE}/{id}.json` que ingiere cada variante). `preguntas-candidatas.md` es el borrador del que salen. |
| `runs/{runId}/` | Lo que escribe cada ejecución: `run.json`, `answers.jsonl`, `indexing.json` y, al puntuar, los CSV. Se versionan las ejecuciones que se citan en la memoria. |
| `ragas/` | Proyecto Python que valida el dataset, puntúa con RAGAS y genera los informes de latencia e indexación (ver su README). |

## Flujo

1. **Modelos en Ollama:** `qwen3-embedding:0.6b` (embeddings) y `qwen2.5:7b-instruct-q4_K_M` (generación y juez). Aspire los descarga en su contenedor; para el scorer, que usa el Ollama del equipo, `ollama pull` de ambos.
2. **Validar el dataset:** `cd eval/ragas && uv run python validate_dataset.py ../dataset`.
3. **Ingerir el corpus** con las cuatro variantes: Aspire arranca un worker por variante (`worker-baseline`, `worker-hierarchical`, `worker-recursive` y `worker-semantic`). En *Documentos*, selecciona los blobs de `corpus.txt` y encólalos con «Las cuatro variantes», o llama a `POST /api/ingestion/enqueue-bulk?pipelineMode=ALL` con la lista; espera a que las cuatro columnas estén en «Procesado». Cada variante escribe en su colección (`document_chunks`, `child_fragments`, `document_chunks_recursive`, `document_chunks_semantic`). Si las colecciones de Qdrant son de otra dimensión (768 con `nomic-embed-text`), hay que borrarlas antes: la comprobación previa del runner lo detecta.
4. **Lanzar el runner**, desde el dashboard de Aspire (recurso `evaluation`, arranque manual) o con:
   ```bash
   dotnet run --project src/AsistenteAyuntamiento.Evaluation -- [--pipelines Baseline,Hierarchical,Recursive,Semantic] [--limit 3] [--seed 42]
   ```
   Sin `--pipelines` pregunta a las cuatro variantes.
   Antes de preguntar nada comprueba que cada colección existe, tiene la dimensión del modelo de embeddings y contiene puntos de todos los documentos del corpus. Responde con `Evaluation:ChatModel` a temperatura 0 en todas las variantes, baraja el orden de las variantes en cada pregunta con la semilla y escribe cada respuesta en cuanto la tiene. Una ejecución interrumpida se continúa con `--resume {runId}`. No abre ningún puerto.
   Con `--score`, al terminar ejecuta también `score.py` y `report.py` (necesita `uv`) e importa la ejecución en la base, todo seguido.
5. **Puntuar e informes:** `uv run python score.py ../runs/{runId}` y `uv run python report.py ../runs/{runId}`.
6. **Guardar en la base de datos:** pulsa ▶ en el recurso `evaluation-import` de Aspire (importa la ejecución más reciente), o `dotnet run --project src/AsistenteAyuntamiento.Evaluation -- --import {runId}` (también acepta `latest`). Lee `run.json`, `summary.csv`, `latency.csv`, `indexing.csv` y `scoring.json`, y guarda la ejecución en `evaluation.EvaluationRuns` y `evaluation.EvaluationPipelineResults` (si ya estaba, la reemplaza). La sección «Evaluación offline» de Métricas la muestra (`GET /api/evaluation/runs`, solo administradores de plataforma). Solo usa la base de datos. Fuera de Aspire, la cadena de conexión se lee de los user secrets del proyecto: `dotnet user-secrets set "ConnectionStrings:asistente-ayuntamiento-db" "Host=localhost;Port=5432;Username=postgres;Password=...;Database=asistente-ayuntamiento-db" --project src/AsistenteAyuntamiento.Evaluation`.

## Elegir qué hace el recurso `evaluation` de Aspire

Por defecto, el ▶ del recurso `evaluation` evalúa todas las preguntas con todas las variantes, puntúa e importa (`--score`). Otro modo se elige con los argumentos en los secretos del AppHost:

```bash
dotnet user-secrets set "Evaluation:Arguments" "--limit 3 --score" --project src/AsistenteAyuntamiento.AppHost
```

| Argumentos | Qué hace |
|---|---|
| `--score` (por defecto) | Evaluación completa, puntuación e importación |
| `--limit 3 --score` | Prueba rápida con 3 preguntas |
| `--pipelines Recursive,Semantic --score` | Solo algunas variantes |
| `--import latest` | Importa la ejecución puntuada más reciente |
| `--latency-report --from 2026-11-01 --to 2026-12-01` | Informe de latencia operativa (necesita `Evaluation:LatencyConnection`, ver abajo) |

Para volver al modo por defecto: `dotnet user-secrets remove "Evaluation:Arguments" --project src/AsistenteAyuntamiento.AppHost`. Los cambios se aplican al reiniciar el AppHost.

## Evaluar en otra máquina (GPU)

La evaluación puede usar el Ollama de otra máquina (por ejemplo un sobremesa con GPU) mientras la base, Qdrant y el corpus siguen en local. En esa máquina: Ollama con `OLLAMA_HOST=0.0.0.0` (y el puerto 11434 abierto), `ollama pull qwen2.5:7b-instruct-q4_K_M` y `ollama pull qwen3-embedding:0.6b`. Aquí:

```bash
dotnet user-secrets set "Evaluation:OllamaEndpoint" "http://<ip>:11434" --project src/AsistenteAyuntamiento.AppHost
```

El recurso `evaluation` usará ese Ollama para las respuestas, los embeddings de las consultas y el juez de RAGAS (`--score` le pasa la misma URL a `score.py`). Las latencias medidas serán las de esa máquina. `run.json` guarda la URL de Ollama usada (`ollama_endpoint`) y, si se configura, una descripción del equipo:

```bash
dotnet user-secrets set "Evaluation:Hardware" "RTX 4060 8 GB, Ollama, Windows" --project src/AsistenteAyuntamiento.AppHost
```

## Informe de latencia operativa (capítulo 8)

Latencias reales de la plataforma en un periodo, a partir de lo que ya guarda la base (`AiCallLogs`, `ArenaBattles`, `IngestionMetrics`, `DocumentJobStates`): respuestas del chat que esperaron los usuarios (media, p50, p95, tiempo al primer token y proporción de fallos) por pipeline, sin las ejecuciones en segundo plano del shadow test ni las de la evaluación; llamadas al LLM por paso; lados de la Arena (incluidas las batallas del shadow test, cuyas dos latencias son ejecuciones reales); ingesta por documento; y fallos de ingesta sobre los documentos terminados cuyo estado se actualizó en el periodo (es una foto del estado actual: un reintento posterior mueve el documento a su periodo). Solo lee agregados de columnas numéricas y de estado, nunca preguntas, respuestas ni identificadores, y abre la sesión en solo lectura.

La cadena de conexión se pasa en `EVAL_LATENCY_DB` y debe ser de un rol de solo lectura; sin ella no se ejecuta. Para crearlo en PostgreSQL:

```sql
CREATE ROLE lector LOGIN PASSWORD '...';
GRANT CONNECT ON DATABASE "asistente-ayuntamiento-db" TO lector;
GRANT USAGE ON SCHEMA chat, arena, ingestion TO lector;
GRANT SELECT ON chat."AiCallLogs", arena."ArenaBattles", ingestion."IngestionMetrics", ingestion."DocumentJobStates" TO lector;
```

Desde Aspire, guarda esa cadena en `Evaluation:LatencyConnection` y el recurso `evaluation` la recibe como `EVAL_LATENCY_DB`:

```bash
dotnet user-secrets set "Evaluation:LatencyConnection" "Host=localhost;Port=5432;Username=lector;Password=...;Database=asistente-ayuntamiento-db" --project src/AsistenteAyuntamiento.AppHost
```

```bash
EVAL_LATENCY_DB="Host=...;Username=lector;Password=...;Database=asistente-ayuntamiento-db" \
  dotnet run --project src/AsistenteAyuntamiento.Evaluation -- --latency-report --from 2026-11-01 --to 2026-12-01
```

Escribe `chat-answers.csv`, `llm-calls.csv`, `arena-sides.csv`, `ingestion.csv` e `ingestion-failures.csv` en `eval/latency/{desde}_{hasta}/` (`--to` excluido; por defecto, los últimos 30 días). Las fechas son días en hora de Madrid. Los percentiles solo cuentan llamadas correctas (los fallos aparecen en `failed_share`), y los dos lados del Arena (`arena_alfa`, `arena_beta`) se agrupan como `arena`, igual que en Métricas. `--from`/`--to` sin `--latency-report`, o `--latency-report` junto a opciones de la evaluación, se rechazan. En este modo el recurso `evaluation` de Aspire no espera a Qdrant ni al modelo.

## Decisiones de la medición

- Todas las variantes recuperan 5 unidades sin umbral de similitud (el chat de la baseline usa 3 con umbral 0,65, calibrado para otro modelo de embeddings).
- La baseline se evalúa con los prompts del chat en streaming, sin preferencias de usuario.
- Las llamadas al LLM de la evaluación se registran con `app.pipeline = evaluation`, para separarlas de las de usuarios reales en `AiCallLogs` y en las métricas.
- Las consultas se embeben con la instrucción de `PromptCatalog.EmbeddingQueryInstruction` (activada con `Ai:Embeddings:UseQueryInstruction`) y los documentos sin ella.
