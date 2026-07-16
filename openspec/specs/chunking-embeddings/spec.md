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
