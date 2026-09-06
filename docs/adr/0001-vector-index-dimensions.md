# ADR 0001: Limitaciones de Índices Vectoriales (pgvector) y Dimensiones de Embeddings

## Fecha
2026-09-06

## Estado
Aceptado

## Contexto
El sistema utiliza dos pipelines de recuperación de información (Retrieval) distintos:
1. **Pipeline Genérico (Baseline):** Emplea la tabla `DocumentChunks` con el modelo `gemini-embedding-004` (768 dimensiones).
2. **Pipeline Jerárquico (Hierarchical):** Emplea la tabla `ChildFragments` con el modelo `qwen/qwen3-embedding-8b` (4096 dimensiones).

Para reducir drásticamente la latencia de las consultas en el chat, se propuso reincorporar los índices `hnsw` de la extensión `pgvector` en ambas tablas. Sin embargo, `pgvector` impone un límite matemático duro: **el tamaño máximo soportado para índices `hnsw` (y también `ivfflat`) sobre el tipo estándar `vector` es de 2.000 dimensiones.**

Si se intenta aplicar el atributo `.HasMethod("hnsw")` a una columna definida como `vector(4096)`, PostgreSQL devuelve el error fatal:
`column cannot have more than 2000 dimensions for hnsw index`.

## Decisión
Se ha decidido aplicar configuraciones asimétricas de indexación en la base de datos:

1. **Tabla `DocumentChunks` (Baseline):** 
   - Se fija la dimensión a `vector(768)`.
   - Se mantiene y aplica el índice `hnsw` con `vector_cosine_ops`.
   - **Razón:** El modelo de Gemini (768 dimensiones) está muy por debajo del límite de pgvector, por lo que podemos beneficiarnos del rendimiento de búsqueda por vecindad aproximada.

2. **Tabla `ChildFragments` (Hierarchical):**
   - Se fija la dimensión a `vector(4096)` para soportar de forma nativa los embeddings de Qwen3.
   - **Se elimina explícitamente el índice `hnsw`.**
   - **Razón:** Al carecer de índice, PostgreSQL aplicará Búsqueda Exacta (*Exact Nearest Neighbor* mediante escaneo secuencial) para este modelo. Es más lento para bases de datos masivas, pero es la única manera de almacenar embeddings de tamaño superior a 2000 sin corromper el esquema o forzar una reducción de dimensionalidad perjudicial.

## Consecuencias
### Positivas
- Se garantiza la estabilidad de las migraciones de Entity Framework Core.
- El chat usando el pipeline Baseline experimenta una caída de la latencia de búsqueda de O(N) a tiempos sub-milisecond gracias a HNSW.
- El sistema es capaz de almacenar modelos de altísima precisión topológica (4096 dims) en el modelo jerárquico.

### Negativas (y Futuros Trabajos)
- A medida que crezca el corpus del BOE/BOJA, las respuestas del Pipeline Jerárquico irán sufriendo latencias mayores en tiempo de ejecución.
- **Solución futura:** Si la velocidad del pipeline Jerárquico se vuelve inaceptable en producción, será necesario migrar a un modelo de embeddings diferente (ej. `nomic-embed-text`, `text-embedding-3-large`) que no supere las 2000 dimensiones o investigar si el API de OpenRouter/Qwen permite el parámetro opcional `dimensions` para acortar artificialmente el tamaño del vector de salida.
