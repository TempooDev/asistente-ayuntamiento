# Evaluación RAGAS

Puntúa las ejecuciones del runner (`eval/runs/{runId}/`) con RAGAS usando Ollama como juez, y calcula hit@1/3/5 y MRR sin LLM.

## Preparación

Requiere [uv](https://docs.astral.sh/uv/) y Python 3.12 (RAGAS no admite versiones posteriores):

```bash
cd eval/ragas
uv python install 3.12
uv sync              # crea .venv con las versiones fijadas en uv.lock
uv run pytest        # pruebas offline con el juez simulado
```

Ollama debe tener el modelo juez y el de embeddings: `ollama pull qwen2.5:7b-instruct-q4_K_M` y `ollama pull qwen3-embedding:0.6b`.

## Validar el dataset

```bash
uv run python validate_dataset.py ../dataset
```

Comprueba campos, ids únicos, categorías (minúsculas con guiones), que `sin-respuesta` no tenga documentos y que cada documento referenciado esté en `corpus.txt`. Sale con código 1 si hay errores.

## Puntuar una ejecución

```bash
uv run python score.py ../runs/<runId> [--dataset ../dataset] [--judge-model M] [--ollama-url URL]
```

| Variable | Por defecto |
|---|---|
| `EVAL_JUDGE_MODEL` | `chat_model` de `run.json` |
| `EVAL_EMBEDDING_MODEL` | `embedding_model` de `run.json` |
| `OLLAMA_URL` | `http://localhost:11434` |
| `HALLUCINATION_THRESHOLD` | `0.8` |
| `EVAL_MAX_WORKERS` / `EVAL_TIMEOUT` / `EVAL_JUDGE_NUM_CTX` | `2` / `300` s / `8192` |

Escribe `scores.csv` (fila por pregunta y pipeline), `summary.csv` (media y desviación por pipeline, tasa de alucinación, fallidas y ausentes) y `scoring.json` (modelos, versión de RAGAS, fecha). Si Ollama no responde o falta un modelo, no escribe nada.

Notas de método:

- `LLMContextPrecisionWithReference`, `LLMContextRecall` y `ResponseRelevancy` usan los contextos recuperados (`contexts[].text`); `Faithfulness` usa el contexto de generación. Juez a temperatura 0; `ResponseRelevancy` con `strictness=1` porque a temperatura 0 las preguntas generadas serían idénticas.
- hit@k y rango recíproco se calculan sobre la posición del contexto (1 = primero); en `sin-respuesta` quedan vacíos.
- Las muestras fallidas (`error_code`) y los errores del juez quedan como NaN, nunca 0.
- `no_answer_detected_rate`: en preguntas `sin-respuesta`, proporción de respuestas que reconocen que los boletines no lo recogen. Heurística por frases ("no he encontrado", "no encuentro", "no dispongo", "no hay información", "no aparece", ...; lista completa en `NOT_COVERED_PHRASES` de `score.py` y en `scoring.json`), sin distinguir mayúsculas ni tildes.

## Latencia e indexación

```bash
uv run python report.py ../runs/<runId>
```

Escribe `latency.csv` (n, media, p50 y p95 por pipeline y etapa, sin calentamiento ni fallidas) e `indexing.csv` a partir de `indexing.json`.
