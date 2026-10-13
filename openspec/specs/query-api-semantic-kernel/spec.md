# Specification: Chat Answers with Semantic Kernel (RAG)

## Purpose
Answer chat questions with retrieval-augmented generation on Microsoft Semantic Kernel: search the bulletin fragments (BOE, BOJA) in Qdrant, build the prompt from `PromptCatalog`, and generate the answer with the right model, streaming it through the chat hub.

## Requirements

### Requirement: Baseline Document Search
`BaselineRagPipeline` SHALL embed the text of the last 3 user messages with the platform embedding generator (never the tenant's chat model) and search the `DocumentChunks` collection through `IVectorStore` (Qdrant, cosine). The non-streaming path SHALL take the top 5 hits; the streaming path the top 3 with a minimum score of 0.65. A failed search SHALL degrade to answering without context.

#### Scenario: Streaming question with matching documents
- **WHEN** a user streams a question and Qdrant returns hits above 0.65
- **THEN** at most 3 fragments SHALL be used as context

#### Scenario: Vector store unavailable
- **WHEN** the embedding or the Qdrant search fails
- **THEN** the pipeline SHALL log a warning and answer without retrieved context instead of failing

### Requirement: Prompt Construction
Every prompt SHALL come from `PromptCatalog`. With retrieved fragments, the pipeline SHALL replace the last user message with a template that contains each fragment's title, public URL, department, publication date and text, followed by the question. When the history has no system message, it SHALL add one: `ChatSystem` (non-streaming) or `StreamingSystem` (streaming with documents), both followed by the user's preferences, or `ConversationalSystem` when streaming without documents. The hub SHALL NOT add a system message of its own.

#### Scenario: Compiling the chat context
- **WHEN** fragments are retrieved for a streamed question
- **THEN** the model SHALL receive `StreamingSystem` with the user context, the conversation history (at most 20 messages) and the last user message rewritten with `StreamingUserTemplate`

#### Scenario: Small talk
- **WHEN** a streamed message retrieves no fragment
- **THEN** the model SHALL receive `ConversationalSystem` and the unmodified history

### Requirement: Tenant Model with Fallback
The baseline pipeline SHALL generate with the tenant's chat model, built by `IChatModelFactory` from the tenant's `AiConfiguration` (or the platform defaults), through `ChatStreamingService`. On HTTP 429 before any text was produced it SHALL switch to the platform fallback model (`Ai:Fallback:*`) and add a notice; model errors (including a model that cannot be created), before or after some text, SHALL be thrown as `LlmStreamInterruptedException` carrying the typed code, so every caller can tell a failed answer from a real one. The chat (`AiChatService`) SHALL turn such an error before any text into the typed message (`[LLM_QUOTA_EXCEEDED]`, `[LLM_NOT_CONFIGURED]`, `[LLM_UNAVAILABLE]`) as the answer, without a shadow test, and SHALL let an error after some text propagate. Each call SHALL be recorded in `AiCallLogs` and in the `gen_ai.*` metrics, as a failure with its error code whenever the model failed.

#### Scenario: Model fails after part of the answer
- **WHEN** the model stream fails with a non-cancellation error after some text was streamed
- **THEN** `ChatStreamingService` SHALL NOT switch to the fallback model, SHALL record the call as failed with the classified code (for example `LLM_UNAVAILABLE`), and SHALL throw `LlmStreamInterruptedException` with that code
- **AND** a stream cancelled by the client SHALL just end

#### Scenario: Model fails before any text
- **WHEN** the model stream fails with a non-cancellation error before any text (and no fallback applies)
- **THEN** `ChatStreamingService` SHALL record the call as failed with the classified code and SHALL throw `LlmStreamInterruptedException` with that code, without streaming a message
- **AND** the chat stream SHALL answer with the typed `[LLM_*]` message and end normally

#### Scenario: Provider not configured
- **WHEN** the tenant's provider needs an API key and none is available
- **THEN** `ChatStreamingService` SHALL throw `LlmStreamInterruptedException` with `LLM_NOT_CONFIGURED`, and the chat stream SHALL return the `[LLM_NOT_CONFIGURED]` message instead of an answer

#### Scenario: Non-streaming completion
- **WHEN** the hub's `SendMessage` asks for a non-streaming answer
- **THEN** the baseline pipeline SHALL answer through `CompleteAsync`, which never throws for model errors and returns no sources

### Requirement: Streaming Routed to the Arena Winner
Streamed answers SHALL come from the pipeline with more Arena wins across all tenants (`WinningPipelineSelector`, cached for 5 minutes; ties and no votes choose `Baseline`). For 40% of streamed answers, the other pipeline SHALL also run in the background (shadow test) and the pair SHALL be stored as a pending `ArenaBattle`.

#### Scenario: Hierarchical pipeline is winning
- **WHEN** `Hierarchical` has more Arena wins than `Baseline`
- **THEN** streamed answers SHALL come from `HierarchicalRagPipeline`: query expansion with the ingestion model, hybrid retrieval (Qdrant `ChildFragments` plus PostgreSQL full text, fused with RRF, top 5) and a plain-language answer from the platform chat model (`Ai:Chat:*`), based only on the latest question

#### Scenario: Hierarchical pipeline finds nothing
- **WHEN** the hierarchical retrieval returns no fragment
- **THEN** the answer SHALL be a fixed message saying that no relevant regulation was found

#### Scenario: Shadow run
- **WHEN** a streamed answer is sampled for a shadow test
- **THEN** the other pipeline SHALL answer the same turn on the background work queue, and the battle SHALL be saved with `Winner = Pending`
