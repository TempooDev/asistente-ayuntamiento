> Collected on 2026-10-13 from the spec review. Section 1 is security and should go first.

## 1. Security

- [x] 1.1 A tenant without its own API key (or whose key cannot be decrypted) uses the platform key `Ai:Chat:ApiKey` against the tenant's provider and `EndpointUrl`, so an organization admin can send the platform key to their own server (`AiConfigurationService.cs:49-53`). Without a tenant key, use the platform provider, model and endpoint too.
- [x] 1.2 The Go command service accepts unauthenticated calls when `INTERNAL_GRPC_KEY` is unset, in any environment (`commandserver/server.go:80-81`). Fail fast outside Development.
- [x] 1.3 A manual `workflow_dispatch` deploy builds and deploys any ref without a green CI (`deploy.yml:9,45`).

## 2. Correctness

- [x] 2.1 The organization usage card shows `successRate` (0–100) through the `percent` pipe (95 → 9,500 %), and labels `totalCalls` as "Llamadas (Éxito)" (`analytics-dashboard.component.html:30`).
- [x] 2.2 `/api/ai/metrics/summary` (organization admins) includes platform-wide `IngestionMetrics`, and labels enrichment calls as "Embeddings". Hierarchical and platform-model calls are never written to `AiCallLogs` (`AiMetricsQueryService.cs:31-41`).
- [x] 2.3 A non-429 provider error after some text was streamed ends the answer silently and is recorded as a success (`ChatStreamingService.cs:171-177`).
- [x] 2.4 A failing Arena side is only logged: the user sees an empty or truncated column and can still vote on it (`ArenaStreamingService.cs:91-94`).
- [x] 2.5 A battle can be voted again: the winner changes and the hub adds another assistant message; the hub also clears `OptionalComment` (`ArenaService.cs:109-117`).
- [x] 2.6 After a Tie vote in chat mode the client shows both answers, but the server stores none, so reloading loses that turn (`chat.store.ts:205-212`).
- [x] 2.7 `HistoryAnalyzerService` throws a NullReferenceException when the model returns `"Topics": null` or `"Locations": null` (only `JsonException` is caught, line 60).
- [x] 2.8 The baseline leaves the state at `Processing` when the JSON deserializes to null, and sets `Processing` with the id from the path but later states with `document.DocumentId` (`DocumentIngestionService.cs:36,50`).
- [x] 2.9 `ProcessBlobRequest.Source` defaults to `"Unknown"`, so the source inferred from the path is never used when a client omits it (`ProcessBlobRequest.cs:6`, `IngestionAdminService.cs:355`).
- [x] 2.10 BOE hierarchical ingestion requires `raw-xml/BOE/{id}.xml`, which the scraper treats as an optional backup (`BoeIngestionService.cs:67` vs `orchestrator.go:368`). Either make the raw store required for BOE or fall back to the JSON text.
- [x] 2.11 `/api/ingestion/reset` truncates all `DocumentJobStates`, including hierarchical statuses whose data stays (`IngestionMaintenance.cs:18`).
- [x] 2.12 Win rates count ties and both-bad votes in the denominator (`ArenaAnalyticsService.cs:54-57`). Decide the definition.
- [x] 2.13 The fragment breadcrumb passes `ElementType` ("General"/"Párrafo") as SUBSECCIÓN, and fragments never get a municipality (`BaseHierarchicalIngestionProcessor.cs:80,89`).
- [x] 2.14 The plain-language generation only labels each parent document by its numeric id, so "Fuentes consultadas" cannot cite the bulletin or norm (`ClearLanguageGenerationService.cs:149`).
- [ ] 2.15 Left over from 2.2: query expansion, ingestion enrichment, history analysis and the non-streaming Arena compare still only reach the `gen_ai.*` metrics, not `AiCallLogs`. Recording them needs a column naming the step (migration) so they are not counted as answers.
- [ ] 2.16 Left over from 2.4: a model error before any text arrives as a normal `[LLM_*]` message, so that Arena side is not marked as failed and can still be voted on.
- [ ] 2.17 When a chat stream fails midway, the partial answer is not saved, so a reload shows the question with no answer.
- [ ] 2.18 The Arena vote reads, checks and writes without a concurrency token: two simultaneous votes could both pass (the client blocks double clicks). Needs a row version (migration).
- [ ] 2.19 Fragments now inherit `ParentDocument.Municipality`, but neither the scraper JSON nor the baseline fills a municipality, so it is always null and the municipality filter never matches a real value.

## 3. Robustness and hygiene

- [ ] 3.1 `IngestionKernel` is transient: with Google, every resolution creates a new `HttpClient`/handler (`Infrastructure/DependencyInjection.cs:200`). Ollama tenant clients are created per request outside the `llm` pool (`ChatModelFactory.cs:49`).
- [ ] 3.2 The Worker has no startup validation of required settings, and no HTTP health endpoint.
- [ ] 3.3 Naming, CA2254 and CA2016 are warnings and CI only runs `dotnet format whitespace`, so they never fail the build.
- [ ] 3.4 `docker-compose.yml` pins every application image to `:latest`, so the `sha-*` tags are unused and there is no rollback procedure.
- [ ] 3.5 `Ai__Embeddings__ChunkMaxLines` in compose is ignored (lines are fixed at 200 in `FixedOverlapChunkingStrategy`).
- [ ] 3.6 The admin endpoint test matches `/api/arena` by exact paths, so a new unprotected Arena admin route would not be caught.
- [ ] 3.7 `IngestionMetricsService` resolves `IAppDbContext` from `IServiceProvider` (service locator).
- [ ] 3.8 Minor: `DocumentJobStates` lives in the `public` schema; `src/go-scraper/.gitignore` does not ignore the `scraper` binary built by `go build ./cmd/scraper`; stale "Azurite" comment in `AppHost.cs:24`; `IReadabilityService` is registered but unused.

## 4. Never implemented (removed from the main specs)

- [ ] 4.1 Analytics:
  - readability (IFSZ) of the answers;
  - Arena confidence intervals, binomial significance (p-value) and wins by criterion (clarity, precision);
  - ingestion cost comparison view (tokens, enrichment calls and time per document per pipeline);
  - CSV/JSON export of the metrics;
  - custom meters `tokens_embedded`, `chunks_generated`, `ingestion_duration_ms` and `arena_votes`.
- [ ] 4.2 Chat:
  - session titles (first 30 characters of the first question);
  - cited sources stored with each answer;
  - clarity/precision and a comment on the chat-mode Arena vote;
  - reveal of the pipelines after a chat-mode vote.
- [ ] 4.3 Hierarchical retrieval:
  - one parent per article, with children per numbered sub-section;
  - fixed answer headings (requirements, deadlines, documents, where to apply);
  - suggesting the citizen office when there is no context.
- [ ] 4.4 Administration:
  - a reprocessing status endpoint;
  - a bulletin filter;
  - select all documents matching the filters, across pages;
  - search by title or content.
- [ ] 4.5 Notifications:
  - `errorMessage` filled in `DocumentStatusChanged`;
  - tenant-scoped events (the `tenant:{id}` groups are joined but unused);
  - persistent, confirmed notification publishing.
- [ ] 4.6 Platform:
  - TLS on the API → scraper gRPC;
  - a custom CA for the filter service;
  - Qdrant key required outside Production;
  - OTLP log export and panic reporting in Go;
  - an in-place edit of a profile interest;
  - a slide animation for the mobile menu.
