# Specification: Chunking and Embedding Creation

## Purpose
Turn the documents stored by the Go scraper into searchable chunks for the baseline pipeline: read the scraper's JSON document from the bucket, split its text with the configured chunking strategy, generate embeddings with the configured model, and store the chunks in PostgreSQL (`DocumentChunks`) and their vectors in Qdrant (`document_chunks`). The same chunking strategy also splits the BOE articles and BOJA texts of the hierarchical pipeline.

## Requirements

### Requirement: Scraper Document Input
`DocumentIngestionService.ProcessBlobAsync(blobPath, source)` SHALL read the JSON document written by the Go scraper (`{ document_id, metadata: { titulo, departamento, fecha_publicacion }, text }`) from the bucket and use `text` as the content. No PDF or HTML parsing happens in .NET.

#### Scenario: Reading a stored document
- **WHEN** the baseline worker receives a message with `blob_path = json/BOE/BOE-A-2026-1234.json`
- **THEN** it SHALL download that blob, deserialize it as `ScrapedDocument` and chunk its `text`

#### Scenario: One document id per run
- **WHEN** the baseline ingests `json/BOE/BOE-A-2026-1234.json`
- **THEN** its job state, chunks and vectors SHALL all use the id from the path (`BOE-A-2026-1234`), as the queue message and the hierarchical pipeline do
- **AND** a different `document_id` inside the JSON SHALL only be logged as a warning

#### Scenario: Blob that is not a document
- **WHEN** the blob is the JSON `null` or is not valid JSON
- **THEN** the document's baseline status SHALL end `Failed` with an error naming the blob (never left in `Processing`)
- **AND** nothing SHALL be chunked or written to the database or the vector store

#### Scenario: Document without text
- **WHEN** the document's `text` is empty
- **THEN** the service SHALL store a single chunk containing the document title (or "Documento sin contenido" when there is no title)

### Requirement: Modular Chunking Strategy
The system SHALL split text through the `IChunkingStrategy` abstraction (`Application.Features.Ingestion.Chunking`), whose `ChunkText(string)` returns `DocumentChunkResult` items (text, zero-based chunk index and strategy metadata). The registered implementation SHALL be `FixedOverlapChunkingStrategy`, a singleton configured with `Ai:Embeddings:ChunkMaxTokens` (default 400), `Ai:Embeddings:ChunkOverlapTokens` (default 50) and `Ai:Embeddings:ChunkMaxLines` (default 200), which splits the text into lines of at most `ChunkMaxLines` tokens (despite its name, the setting is a token limit per line) and groups them into paragraphs with Semantic Kernel's `TextChunker`. `ChunkMaxTokens` and `ChunkMaxLines` SHALL be greater than 0, and `ChunkOverlapTokens` SHALL be at least 0 and lower than `ChunkMaxTokens`; otherwise registering the services SHALL throw `ArgumentOutOfRangeException`, so the host fails at startup.

#### Scenario: Chunking a long document
- **WHEN** a text of several thousand tokens is chunked with the default configuration
- **THEN** it SHALL be split into chunks of at most about 400 tokens with about 50 tokens of overlap
- **AND** each chunk SHALL carry its index and the metadata `Strategy = FixedOverlap`

#### Scenario: Line length configured
- **WHEN** `Ai:Embeddings:ChunkMaxLines` is set (for example through `AI_EMBEDDINGS_CHUNK_MAX_LINES` in `docker-compose.yml`)
- **THEN** the registered strategy SHALL cut lines at that many tokens instead of 200

#### Scenario: Invalid chunking settings
- **WHEN** `ChunkMaxLines` or `ChunkMaxTokens` is 0 or negative, or `ChunkOverlapTokens` is negative or not lower than `ChunkMaxTokens`
- **THEN** `AddApplicationServices` SHALL throw `ArgumentOutOfRangeException`

#### Scenario: Empty text
- **WHEN** `ChunkText` receives an empty or whitespace text
- **THEN** it SHALL return no chunks

#### Scenario: Shared by both pipelines
- **WHEN** the hierarchical pipeline splits a BOE article or a BOJA text
- **THEN** it SHALL use the same registered `IChunkingStrategy`

### Requirement: Only Bulletins in Scope
The baseline pipeline SHALL only ingest documents whose source is a `BulletinType` (BOE, BOJA), compared case-insensitively.

#### Scenario: Document from another source
- **WHEN** `DocumentIngestionService` receives a document whose source is not BOE or BOJA
- **THEN** it SHALL log a warning naming the source and the document
- **AND** return without chunking, generating embeddings or writing to the database, and without failing the message

### Requirement: Embedding Generation and Storage
The system SHALL generate the embeddings of all chunks of a document in batches of 100 with the embedding generator registered in the Semantic Kernel, then store the chunks in PostgreSQL and their vectors in Qdrant. The generator is configured with `Ai:Embeddings:Provider` (`ollama` by default, `google`, `openai` or `openrouter`), `Ai:Embeddings:Model` (`nomic-embed-text` only when no model is configured), `Ai:Embeddings:EndpointUrl` and `Ai:Embeddings:ApiKey`.

#### Scenario: Storing a document's chunks
- **WHEN** a document yields N chunks
- **THEN** its previous `DocumentChunks` rows SHALL be deleted and the N new rows inserted in one transaction, each with `DocumentId`, `Source`, `Title`, `Department`, `Content`, `ChunkIndex` and `PublicationDate`
- **AND** after the commit, the document's previous points SHALL be deleted from `document_chunks` and N points upserted, using the chunk ids as point ids

#### Scenario: Embedding model not configured
- **WHEN** neither `Ai:Embeddings:Provider` nor `Ai:Embeddings:Model` is set
- **THEN** the system SHALL use Ollama's `nomic-embed-text` at the `ollama` connection string (or `http://localhost:11434`)

### Requirement: Massive Asynchronous Reprocessing via RabbitMQ
The system SHALL support enqueuing every stored document for asynchronous ingestion, so that thousands of blobs are processed by the Workers without blocking the API.

#### Scenario: Enqueuing every stored blob
- **WHEN** a platform administrator calls `POST /api/ingestion/reprocess-all`
- **THEN** the API SHALL list every blob under `json/` in the bucket and publish a `DocumentMessage` per blob to `documents_to_process_baseline` and/or `documents_to_process_hierarchical`, according to `pipelineMode`
- **AND** `BaselineIngestionConsumer` and `HierarchicalIngestionConsumer` in the Workers SHALL consume the messages with the retry and dead-letter policy of `RabbitMqConsumerBase`

## Architecture Decision Records (ADR)

### ADR: Configurable Embedding Provider (OpenRouter and Qwen3-Embedding-8B)

**Decision:** The embedding provider and model are configuration (`Ai:Embeddings:*`), not code. Besides Ollama and Google, OpenAI-compatible providers such as OpenRouter are supported, so `qwen/qwen3-embedding-8b` can be used instead of (or next to) Google's `text-embedding-004`, which is the default of `docker-compose.yml`.

**Context and Justification:**
- **Quality in legal Spanish:** `text-embedding-004` handles standard vocabulary well but is weaker with long sentences and negations; `Qwen3-Embedding-8B` resolves complex syntax better and ranks high in multilingual benchmarks (MTEB).
- **Chunk size:** chunk size does not depend on the model's context window. Both pipelines use `FixedOverlapChunkingStrategy` with about 400 tokens per chunk and 50 of overlap (`Ai:Embeddings:ChunkMaxTokens`, `Ai:Embeddings:ChunkOverlapTokens`); a larger window only leaves headroom for the enrichment text added to hierarchical fragments.
- **Cost:** Qwen3-Embedding-8B through OpenRouter costs about the same or less per million tokens (~$0.01/1M) than Google (~$0.02/1M); API cost is not a limiting factor.
- **Impact:** Qwen produces 4,096-dimensional vectors. pgvector indexes are limited to 2,000 dimensions, so vectors are stored and searched in Qdrant (see `qdrant-vector-store`); PostgreSQL keeps the text. Changing the embedding model changes the vector size, and the existing Qdrant collections must be recreated (the ingestion refuses to write to a collection of another size).
