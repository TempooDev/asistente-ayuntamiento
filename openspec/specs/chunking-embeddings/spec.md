# Specification: Chunking and Embedding Creation

## Purpose
Injest raw documents from storage, extract their plain text, decompose them into semantic chunks, generate embedding vectors, and persist them in pgvector.

## Requirements

### Requirement: Document Parsing and Text Extraction
The system SHALL retrieve raw documents and extract plain text.

#### Scenario: Extracting PDF document text
- **WHEN** a document scraped event is received
- **THEN** the system SHALL download the raw stream from storage and extract text using PdfPig

### Requirement: Text Chunking
The system SHALL divide plain text into smaller, overlapping semantic fragments.

#### Scenario: Running Semantic Kernel TextChunker
- **WHEN** raw text is extracted
- **THEN** the system SHALL split the text using `maxTokensPerLine: 100` and `maxTokensPerParagraph: 300` with `overlapTokens: 50`

### Requirement: Embedding Generation and Storage
The system SHALL batch-generate embedding vectors for all paragraphs and save them.

#### Scenario: Saving vectors in pgvector
- **WHEN** chunks are created
- **THEN** the system SHALL batch generate embeddings using `nomic-embed-text`
- **AND** store each chunk and its floating-point vector inside PostgreSQL database

### Requirement: Massive Asynchronous Reprocessing via RabbitMQ
The system SHALL support triggering a massive ingestion flow via RabbitMQ to process thousands of S3 blobs asynchronously.

#### Scenario: Enqueuing blobs for reprocessing
- **WHEN** the `POST /api/ingestion/reprocess-all` endpoint is called
- **THEN** the API SHALL list all JSON blobs in the S3 bucket
- **AND** publish a `DocumentMessage` to the `documents_to_process` RabbitMQ queue for each blob
- **AND** the `RabbitMqConsumerService` background worker SHALL consume the messages safely without blocking the API.

## Architecture Decision Records (ADR)

### ADR: Migration to OpenRouter and Qwen3-Embedding-8B

**Decision:** We have decided to support OpenRouter to utilize `qwen/qwen3-embedding-8b` instead of (or as the primary alternative to) Google's `text-embedding-004`, introducing configurable parameters to maximize the chunking window.

**Context and Legal/Technical Justification:**
- **Quality in Legal/Formal Context (Spanish):** 
  - Google `text-embedding-004`: Good for standard vocabulary and translations, but weak with long sentences and negations.
  - `Qwen3-Embedding-8B`: Outstanding at resolving complex syntax. Robust due to its 8B parameter size and a top performer in multilingual rankings (MTEB).
- **Chunking and Coherence:**
  - Google: Max ~1,500 words per chunk.
  - Qwen: Up to ~6,000 words per chunk without losing coherence.
- **Cost Analysis (Negligible difference):**
  - Qwen3-Embedding-8B via OpenRouter is equal to or slightly cheaper per million tokens (~$0.01 USD/1M tokens) compared to Google (~$0.02 USD/1M tokens).
  - Cost for 100M tokens (~150,000 pages): ~$2.00 USD (Google) vs ~$1.00 USD (Qwen).
  - API cost is not a limiting factor. Higher ROI with Qwen by leveraging the reasoning capabilities of an 8B model at a lightweight price point.
- **Impact:** Qwen embeddings have higher dimensionality by default. This does not increase API costs but will require slightly more RAM and disk space in the vector database (pgvector). However, the performance of semantic legal search will improve substantially.
