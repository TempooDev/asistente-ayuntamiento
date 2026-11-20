## MODIFIED Requirements

### Requirement: Embedding Generator Resolution
Retrieval SHALL resolve the embedding generator from the platform `Kernel`, configured by `Ai:Embeddings:*` (provider `ollama` by default, or `google`, `openai`, `openrouter`), never from a per-tenant chat client.

#### Scenario: Non-streaming completion with a user message
- **WHEN** a question with a non-empty user message reaches the baseline pipeline (the chat only streams: the non-streaming completion was removed)
- **THEN** the query embedding SHALL be generated with the platform embedding generator, whatever the tenant's chat provider
