## MODIFIED Requirements

### Requirement: Baseline Document Search
`BaselineRagPipeline` SHALL embed the text of the last 3 user messages with the platform embedding generator (never the tenant's chat model) and search the `DocumentChunks` collection through `IVectorStore` (Qdrant, cosine). The streaming path SHALL take the top 3 hits with a minimum score of 0.65; the offline evaluation path the top 5 without a threshold. A failed search SHALL degrade to answering without context.

#### Scenario: Streaming question with matching documents
- **WHEN** a user streams a question and Qdrant returns hits above 0.65
- **THEN** at most 3 fragments SHALL be used as context

#### Scenario: Vector store unavailable
- **WHEN** the embedding or the Qdrant search fails
- **THEN** the pipeline SHALL log a warning and answer without retrieved context instead of failing

### Requirement: Prompt Construction
Every prompt SHALL come from `PromptCatalog`. With retrieved fragments, the pipeline SHALL replace the last user message with `StreamingUserTemplate`, which contains each fragment's title, public URL, department, publication date and text, followed by the question. The template SHALL tell the model to skip documents unrelated to the question while using the others, and to say so when neither the documents nor the conversation contain the requested fact; it SHALL NOT tell the model to ignore the whole context. When the history has no system message, the pipeline SHALL add one: `StreamingSystem` followed by the user's preferences when documents were retrieved, or `ConversationalSystem` otherwise. The hub SHALL NOT add a system message of its own.

#### Scenario: Compiling the chat context
- **WHEN** fragments are retrieved for a streamed question
- **THEN** the model SHALL receive `StreamingSystem` with the user context, the conversation history (at most 20 messages) and the last user message rewritten with `StreamingUserTemplate`

#### Scenario: One retrieved fragment is off topic
- **WHEN** three fragments are retrieved and one of them is about another subject
- **THEN** the prompt SHALL ask the model to answer from the other two instead of discarding all the context

#### Scenario: Small talk
- **WHEN** a streamed message retrieves no fragment
- **THEN** the model SHALL receive `ConversationalSystem` and the unmodified history

### Requirement: Tenant Model with Fallback
The baseline pipeline SHALL generate with the tenant's chat model, built by `IChatModelFactory` from the tenant's `AiConfiguration` (or the platform defaults), through `ChatStreamingService`. On HTTP 429 before any text was produced it SHALL switch to the platform fallback model (`Ai:Fallback:*`) and add a notice; model errors (including a model that cannot be created), before or after some text, SHALL be thrown as `LlmStreamInterruptedException` carrying the typed code, so every caller can tell a failed answer from a real one. The chat (`AiChatService`) SHALL turn such an error before any text into the typed message (`[LLM_QUOTA_EXCEEDED]`, `[LLM_NOT_CONFIGURED]`, `[LLM_UNAVAILABLE]`) as the answer, without a shadow test, and SHALL let an error after some text propagate. Each call SHALL be recorded in `AiCallLogs` and in the `gen_ai.*` metrics, as a failure with its error code whenever the model failed. The chat SHALL only answer by streaming; there is no non-streaming completion path.

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
- **WHEN** a client invokes `SendMessage` on the chat hub to get a non-streaming answer
- **THEN** the hub SHALL reject the call because the method no longer exists, and answers SHALL only be produced by the streaming method
