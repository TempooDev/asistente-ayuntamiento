# Specification: Extensible Chunking Strategies & Classic Overlapping Chunk Pipeline

## Purpose
Establish an extensible chunking architecture in `AsistenteAyuntamiento.Application.Features.Ingestion` to support empirical benchmarking and comparative testing between diverse chunk generation strategies, while formalizing the classic fixed-size sliding-window overlapping chunk implementation.

## Requirements

### Requirement: Modular Chunking Strategy Abstraction
The system SHALL provide an abstraction `IChunkingStrategy` to decouple text decomposition from document ingestion and persistence.

#### Scenario: Running a chunking strategy
- **WHEN** plain text and optional metadata are supplied to an `IChunkingStrategy` implementation
- **THEN** it SHALL return a list of `DocumentChunkResult` records containing the chunk text, chunk index, and any strategy-specific metadata.

### Requirement: Fixed-Size Text Chunking with Overlap
The system SHALL provide a default `FixedOverlapChunkingStrategy` utilizing Semantic Kernel's `TextChunker`.

#### Scenario: Splitting text into overlapping chunks
- **WHEN** raw text is passed to `FixedOverlapChunkingStrategy`
- **THEN** it SHALL split plain text into lines up to `ChunkMaxLines` (default: 200)
- **AND** decompose lines into paragraphs with `ChunkMaxTokens` (default: 400) and `ChunkOverlapTokens` (default: 50)
- **AND** `DocumentIngestionService` SHALL vectorize each chunk and persist into PostgreSQL `DocumentChunks` and Qdrant collection `document_chunks`.

### Requirement: Discard BOPMA Documents
All chunking and ingestion pipelines SHALL discard any document with source `BOPMA`.

#### Scenario: Processing a BOPMA document
- **WHEN** a document with source `BOPMA` is received by any ingestion processor
- **THEN** the processor SHALL log an informative warning stating that BOPMA ingestion is discarded
- **AND** skip chunking, embedding generation, and database insertion without failing.
