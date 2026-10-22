## MODIFIED Requirements

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

#### Scenario: Hierarchical pipeline splitting
- **WHEN** the hierarchical pipeline splits a BOE article or a BOJA text
- **THEN** it SHALL use the unkeyed `IChunkingStrategy` (`FixedOverlap`), whatever flat variants are enabled

## ADDED Requirements

### Requirement: Query Embedding Instruction
When `Ai:Embeddings:QueryInstruction` is not empty, every embedding of a user query used for retrieval (baseline and flat-variant search, hybrid retrieval, Arena) SHALL be generated from `Instruct: {QueryInstruction}\nQuery: {query}`. Texts embedded at ingestion time, including the semantic strategy's sentence windows, SHALL be embedded without the prefix. When the setting is empty or missing, queries SHALL be embedded unchanged. The local AppHost SHALL use `qwen3-embedding:0.6b` with a Spanish instruction for municipal bulletin retrieval.

#### Scenario: Instruction configured
- **WHEN** `QueryInstruction` is set and a user asks "¿Qué plazo tengo para presentarme?"
- **THEN** the embedded text SHALL be `Instruct: {QueryInstruction}\nQuery: ¿Qué plazo tengo para presentarme?`
- **AND** the chunks of the corpus SHALL have been embedded without any prefix

#### Scenario: No instruction
- **WHEN** `QueryInstruction` is empty (for example in production with `text-embedding-004`)
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
`SemanticChunkingStrategy` SHALL split the text into sentences with a splitter that does not cut after the abbreviations common in bulletins (`art.`, `arts.`, `núm.`, `apdo.`, `Sr.`, `Sra.`, `D.`, `Dña.`, `etc.`, ordinals such as `1.º`), embed each sentence together with `BufferSize` neighbours on each side (default 1) with the platform embedding generator in batches of 100, and start a new chunk where the cosine distance between consecutive windows is above the `BreakpointPercentile` (default 95) of all distances of the document. Chunks under `MinTokens` (default 100) SHALL be merged with the next one, and chunks over `ChunkMaxTokens` SHALL be split with `RecursiveChunkingStrategy`. Settings come from `Ai:Chunking:Semantic:*`. The tokens embedded for chunking SHALL be returned in the result so ingestion metrics include them.

#### Scenario: Topic change
- **WHEN** a document has six sentences about a tax ordinance followed by six about a job offer
- **THEN** the strategy SHALL place a breakpoint between the two groups and return at least two chunks, each with `Strategy = Semantic`

#### Scenario: Abbreviations are not sentence ends
- **WHEN** a sentence contains "conforme al art. 25 de la Ley 7/1985"
- **THEN** the splitter SHALL keep it as one sentence

#### Scenario: Short document
- **WHEN** the text has a single sentence or is under `MinTokens`
- **THEN** it SHALL return one chunk with the whole text

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
