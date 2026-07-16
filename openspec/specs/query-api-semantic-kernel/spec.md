# Specification: API for Querying with Semantic Kernel

## Purpose
Expose a backend service orchestrating semantic search queries and RAG text completions via Microsoft Semantic Kernel.

## Requirements

### Requirement: Document Context Search
The system SHALL retrieve relevant document fragments matching the semantic meaning of the user query.

#### Scenario: Running pgvector similar search
- **WHEN** a user query is received
- **THEN** the system SHALL calculate the query embedding vector and query PostgreSQL for the top K closest chunks ordered by Cosine Distance

### Requirement: Contextual Prompt Generation (RAG)
The system SHALL combine the prompt templates, history, and retrieved document contexts into a single LLM request.

#### Scenario: Compiling Chat Context
- **WHEN** similar chunks are successfully retrieved
- **THEN** the system SHALL compile a prompt combining `SystemPrompt.txt`, `RagPrompt.txt` (with context chunks injected), and the session's chat history
- **AND** feed the compiled prompt to the local LLM chat completion service
