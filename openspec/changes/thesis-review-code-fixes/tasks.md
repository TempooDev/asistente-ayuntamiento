## 1. Code

- [x] 1.1 Rewrite `StreamingUserTemplate` without the «IGNORA EL CONTEXTO» clause
- [x] 1.2 Remove `ChatSystem`, `ChatUserTemplate`, `UserContextGuidanceWithDocuments`, `SendMessage`, `GetCompletionAsync`, `CompleteAsync` and `AiCompletionResult`, with their tests
- [x] 1.3 BOJA public URL from the document id
- [x] 1.4 Plain-keyword `query_lexica` and space-joined fallback
- [x] 1.5 Persist the Data Protection key ring in PostgreSQL (migration `DataProtectionKeys`)

## 2. Verification

- [x] 2.1 Unit, API, worker, architecture and infrastructure tests pass
- [ ] 2.2 Rerun the E3/E4 flat variants with the new template to measure the change in abstentions
- [ ] 2.3 After the first deploy, enter again once the tenant keys saved before it (they were encrypted with the old, unpersisted key ring)
