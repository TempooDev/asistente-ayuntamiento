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
The system SHALL split text through the `IChunkingStrategy` abstraction (`Application.Features.Ingestion.Chunking`), whose `ChunkTextAsync(string, CancellationToken)` returns `DocumentChunkResult` items (text, zero-based chunk index and strategy metadata). Three implementations SHALL be registered as keyed singletons by strategy name: `FixedOverlap` (`FixedOverlapChunkingStrategy`), `Recursive` (`RecursiveChunkingStrategy`) and `Semantic` (`SemanticChunkingStrategy`); the unkeyed `IChunkingStrategy` SHALL be `FixedOverlap`. `FixedOverlapChunkingStrategy` is configured with `Ai:Embeddings:ChunkMaxTokens` (default 400), `Ai:Embeddings:ChunkOverlapTokens` (default 50) and `Ai:Embeddings:ChunkMaxLines` (default 200), splits the text into lines of at most `ChunkMaxLines` tokens and groups them into paragraphs with Semantic Kernel's `TextChunker`. `ChunkMaxTokens` and `ChunkMaxLines` SHALL be greater than 0, and `ChunkOverlapTokens` SHALL be at least 0 and lower than `ChunkMaxTokens`; otherwise registering the services SHALL throw `ArgumentOutOfRangeException`, so the host fails at startup. All three strategies SHALL count tokens with the same estimator.

#### Scenario: Chunking a long document
- **WHEN** a text of several thousand tokens is chunked with the fixed strategy and the default configuration
- **THEN** it SHALL be split into chunks of at most about 400 tokens with about 50 tokens of overlap
- **AND** each chunk SHALL carry its index and the metadata `Strategy = FixedOverlap`

#### Scenario: Line length configured
- **WHEN** `Ai:Embeddings:ChunkMaxLines` is set (for example through `AI_EMBEDDINGS_CHUNK_MAX_LINES` in `docker-compose.yml`)
- **THEN** the fixed strategy SHALL cut lines at that many tokens instead of 200

#### Scenario: Invalid chunking settings
- **WHEN** `ChunkMaxLines` or `ChunkMaxTokens` is 0 or negative, or `ChunkOverlapTokens` is negative or not lower than `ChunkMaxTokens`
- **THEN** `AddApplicationServices` SHALL throw `ArgumentOutOfRangeException`

#### Scenario: Empty text
- **WHEN** any strategy receives an empty or whitespace text
- **THEN** it SHALL return no chunks, and the semantic strategy SHALL NOT call the embedding model

#### Scenario: Shared by both pipelines
- **WHEN** the hierarchical pipeline splits a BOE article or a BOJA text
- **THEN** it SHALL use the unkeyed `IChunkingStrategy` (`FixedOverlap`), whatever flat variants are enabled

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

### Requirement: Query Embedding Instruction
When `Ai:Embeddings:UseQueryInstruction` is `true`, every embedding of a user query used for retrieval (baseline and flat-variant search, hybrid retrieval, Arena) SHALL be generated from `Instruct: {instruction}\nQuery: {query}`, where the instruction is `PromptCatalog.EmbeddingQueryInstruction`, kept with the other prompts. Texts embedded at ingestion time, including the semantic strategy's sentence windows, SHALL be embedded without the prefix. When the setting is `false` or missing, queries SHALL be embedded unchanged. The local AppHost SHALL use `qwen3-embedding:0.6b` with the instruction on; the instruction is written in English, as the model's authors advise.

#### Scenario: Instruction configured
- **WHEN** `UseQueryInstruction` is `true` and a user asks "¿Qué plazo tengo para presentarme?"
- **THEN** the embedded text SHALL be `Instruct: {instruction}\nQuery: ¿Qué plazo tengo para presentarme?`
- **AND** the chunks of the corpus SHALL have been embedded without any prefix

#### Scenario: No instruction
- **WHEN** `UseQueryInstruction` is off (for example in production with `text-embedding-004`)
- **THEN** the query SHALL be embedded exactly as written

### Requirement: Recursive Chunking Strategy
`RecursiveChunkingStrategy` SHALL split text by trying, in order, paragraph breaks (`\n\n`), line breaks (`\n`), sentence ends (`. `, `; `, `: `), spaces and finally characters: a piece within `ChunkMaxTokens` SHALL be kept whole, and a longer piece SHALL be split again with the next separator. Adjacent pieces SHALL be merged up to `ChunkMaxTokens`, and consecutive chunks SHALL share up to `ChunkOverlapTokens` taken from whole pieces at the end of the previous chunk. Its limits SHALL default to the fixed strategy's (`Ai:Chunking:Recursive:ChunkMaxTokens`, `Ai:Chunking:Recursive:ChunkOverlapTokens`, falling back to `Ai:Embeddings:*`) and are validated like them.

#### Scenario: Paragraphs that fit
- **WHEN** a text has three paragraphs of 150 tokens each and the limit is 400
- **THEN** the first chunk SHALL contain the first two paragraphs whole and the second chunk the third, without cutting any paragraph
- **AND** each chunk SHALL carry `Strategy = Recursive`

#### Scenario: Paragraph longer than the limit
- **WHEN** one paragraph has 900 tokens with several lines and sentences
- **THEN** it SHALL be split at line breaks or, if a line is still too long, at sentence ends, and no chunk SHALL exceed 400 tokens

#### Scenario: Text without separators
- **WHEN** a 1,000-token text contains no whitespace or punctuation
- **THEN** it SHALL be cut by characters into chunks of at most 400 tokens

### Requirement: Semantic Chunking Strategy
`SemanticChunkingStrategy` SHALL split the text into sentences with a splitter that does not cut after the abbreviations common in bulletins (`art.`, `arts.`, `núm.`, `apdo.`, `Sr.`, `Sra.`, `D.`, `Dña.`, `etc.`, ordinals such as `1.º`), embed each sentence together with `BufferSize` neighbours on each side (default 1) with the platform embedding generator in batches of 100, and start a new chunk where the cosine distance between consecutive windows is above the `BreakpointPercentile` (default 95) of all distances of the document. Chunks under `MinTokens` (default 100, and at most a quarter of `ChunkMaxTokens` when it is not configured) SHALL be merged with the next one, and chunks over `ChunkMaxTokens` SHALL be split with `RecursiveChunkingStrategy`. Before embedding, a "sentence" over `ChunkMaxTokens` (a table or list without full stops) SHALL be split the same way, and each sentence window SHALL be limited to `4 × ChunkMaxTokens` tokens by taking fewer neighbours, so no embedding call exceeds that budget. Settings come from `Ai:Chunking:Semantic:*`. The tokens embedded for chunking SHALL be returned in the result so ingestion metrics include them.

#### Scenario: Topic change
- **WHEN** a document has six sentences about a tax ordinance followed by six about a job offer
- **THEN** the strategy SHALL place a breakpoint between the two groups and return at least two chunks, each with `Strategy = Semantic`

#### Scenario: Abbreviations are not sentence ends
- **WHEN** a sentence contains "conforme al art. 25 de la Ley 7/1985"
- **THEN** the splitter SHALL keep it as one sentence

#### Scenario: Short document
- **WHEN** the text has a single sentence or is under `MinTokens`
- **THEN** it SHALL return one chunk with the whole text

#### Scenario: Long text without sentence ends
- **WHEN** the text is a table of thousands of tokens without full stops
- **THEN** every window sent to the embedding model SHALL be at most `4 × ChunkMaxTokens` tokens
- **AND** every returned chunk SHALL be at most `ChunkMaxTokens` tokens

#### Scenario: Embedding model fails
- **WHEN** the embedding call fails while chunking
- **THEN** `ChunkTextAsync` SHALL throw, so the document fails and is retried by the consumer's policy, instead of silently falling back to another strategy

### Requirement: Flat-Chunk RAG Variants
The baseline ingestion and answering code SHALL serve three flat-chunk variants, each defined by a `FlatChunkVariant` (pipeline type, keyed chunking strategy, Qdrant collection): `Baseline` (`FixedOverlap`, `document_chunks`), `Recursive` (`Recursive`, `document_chunks_recursive`) and `Semantic` (`Semantic`, `document_chunks_semantic`). `DocumentChunks` SHALL have a `ChunkingStrategy` column (`FixedOverlap` for rows created before this change), and the ingestion of one variant SHALL delete and insert only the rows of its own strategy and the points of its own collection. The ingestion metrics SHALL be recorded with the variant's `PipelineType`.

#### Scenario: Two variants of the same document
- **GIVEN** `BOE-A-2026-1234` was ingested by `Baseline`
- **WHEN** the `Recursive` variant ingests it
- **THEN** its `FixedOverlap` rows and its points in `document_chunks` SHALL be unchanged
- **AND** its `Recursive` rows SHALL be replaced and its points in `document_chunks_recursive` upserted

#### Scenario: Answering with a flat variant
- **WHEN** the `Semantic` pipeline answers a question
- **THEN** it SHALL search only `document_chunks_semantic`, with the same limits, score threshold, prompts and model selection as the baseline

#### Scenario: Existing chunks after the migration
- **WHEN** the migration runs on a database with baseline chunks
- **THEN** every existing `DocumentChunks` row SHALL have `ChunkingStrategy = FixedOverlap` and the baseline SHALL keep answering from them without re-ingestion

## Architecture Decision Records (ADR)

### ADR: Configurable Embedding Provider (OpenRouter and Qwen3-Embedding-8B)

**Decision:** The embedding provider and model are configuration (`Ai:Embeddings:*`), not code. Besides Ollama and Google, OpenAI-compatible providers such as OpenRouter are supported, so `qwen/qwen3-embedding-8b` can be used instead of (or next to) Google's `text-embedding-004`, which is the default of `docker-compose.yml`.

**Context and Justification:**
- **Quality in legal Spanish:** `text-embedding-004` handles standard vocabulary well but is weaker with long sentences and negations; `Qwen3-Embedding-8B` resolves complex syntax better and ranks high in multilingual benchmarks (MTEB).
- **Chunk size:** chunk size does not depend on the model's context window. Both pipelines use `FixedOverlapChunkingStrategy` with about 400 tokens per chunk and 50 of overlap (`Ai:Embeddings:ChunkMaxTokens`, `Ai:Embeddings:ChunkOverlapTokens`); a larger window only leaves headroom for the enrichment text added to hierarchical fragments.
- **Cost:** Qwen3-Embedding-8B through OpenRouter costs about the same or less per million tokens (~$0.01/1M) than Google (~$0.02/1M); API cost is not a limiting factor.
- **Impact:** Qwen produces 4,096-dimensional vectors. pgvector indexes are limited to 2,000 dimensions, so vectors are stored and searched in Qdrant (see `qdrant-vector-store`); PostgreSQL keeps the text. Changing the embedding model changes the vector size, and the existing Qdrant collections must be recreated (the ingestion refuses to write to a collection of another size).
