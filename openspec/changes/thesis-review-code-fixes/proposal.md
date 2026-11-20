## Why

The technical review of the thesis (November 2026) found five small defects in the chat code:
- The flat pipelines' user template told the model to ignore the context when it was off topic. It was suspected of causing the abstentions the chunking study measured; rerunning E3/E4 showed it was not (the retrieved chunks lacked the facts), but the clause was still wrong.
- `ChatSystem` and the hub's `SendMessage` were dead code: the client only streams.
- BOJA citations pointed to the BOJA home page instead of the cited document.
- The query expansion prompt asked for `tsquery` syntax (`a & b`), but the search uses `websearch_to_tsquery`, which does not treat `&` as an operator.
- The Data Protection key ring lived in the container's file system. A redeploy therefore made the stored tenant API keys unreadable.

## What Changes

- `StreamingUserTemplate` no longer has the «IGNORA EL CONTEXTO» clause. It now says to skip the documents that do not match the question, use the others, and say so when nothing answers it.
- Removed: `PromptCatalog.ChatSystem`, `ChatUserTemplate` and `UserContextGuidanceWithDocuments`; `ChatHub.SendMessage`; `IAiChatService.GetCompletionAsync`; `IChatStreamingService.CompleteAsync`; `AiCompletionResult`.
- `GetPublicUrl` builds `https://www.juntadeandalucia.es/boja/{year}/{number}/{order}` from a `BOJA-YYYY-N-O` id. Ids in any other format still fall back to the BOJA home page.
- `query_lexica` is now 2 to 4 plain keywords separated by spaces, with no operators. The fallback joins the user's words with spaces.
- The key ring is stored in PostgreSQL (`identity."DataProtectionKeys"`) under the application name `AsistenteAyuntamiento`, through `AddInfrastructureServices`, which is shared by the API, the workers and the evaluation runner. This adds the migration `DataProtectionKeys`.

## Capabilities

### Modified Capabilities
- `query-api-semantic-kernel`
- `llm-provider-security`
- `contexto-personalizado`
- `hierarchical-retrieval`
- `user-api-keys-vault`

## Impact

- Application (prompts, pipelines, chat service), ApiService (hub), Infrastructure (DbContext, DI, migration), tests.
- The chunking study runs (E1–E4) used the old template. Their abstention figures describe that template, not the current one.
