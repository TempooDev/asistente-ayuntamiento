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

## Decisiones de la medición

- Todas las variantes recuperan 5 unidades sin umbral de similitud (el chat de la baseline usa 3 con umbral 0,65, calibrado para otro modelo de embeddings).
- La baseline se evalúa con los prompts del chat en streaming, sin preferencias de usuario.
- Las llamadas al LLM de la evaluación se registran con `app.pipeline = evaluation`, para separarlas de las de usuarios reales en `AiCallLogs` y en las métricas.
- Las consultas se embeben con la instrucción de `PromptCatalog.EmbeddingQueryInstruction` (activada con `Ai:Embeddings:UseQueryInstruction`) y los documentos sin ella.
