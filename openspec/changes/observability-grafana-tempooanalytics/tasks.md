> **Revised 2026-10-13.** Tasks updated to the current code. Some work already exists and is marked done. Implement after phase 3 of `audit-remediation-2026-10` (see design, decision 5). Section 4 (TempooAnalytics) is blocked until its SDK or API is available.

## 1. OpenTelemetry Backbone

- [x] 1.1 OpenTelemetry in the ApiService and Worker through `AsistenteAyuntamiento.ServiceDefaults/Extensions.cs` (ASP.NET Core, HttpClient, runtime, OTLP exporter). EF Core/Npgsql and RabbitMQ tracing come from the Aspire client integrations.
- [ ] 1.2 Verify the trace continuity API → RabbitMQ → Worker in the Aspire dashboard (one trace for an ingestion request). If it is broken, inject and extract `traceparent` in `RabbitMqConsumerBase` (`audit-remediation-2026-10` task 3.2).
- [ ] 1.3 Enable gRPC client instrumentation in `ServiceDefaults/Extensions.cs:70-71` (add `OpenTelemetry.Instrumentation.GrpcNetClient` to `Directory.Packages.props`).
- [x] 1.4 Go: tracer and meter providers with W3C propagation (`src/go-scraper/internal/telemetry/otel.go`).
- [ ] 1.5 Go: add the `otelgrpc` stats handlers to `internal/commandserver/server.go` and `internal/filterclient/client.go`. Inject `traceparent`/`tracestate` into the AMQP headers in `internal/messaging/rabbitmq.go`, following `docs/messaging-contract.md`.
- [ ] 1.6 Add `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES=deployment.environment=production` to `apiservice`, `worker-*` and `go-scraper` in `docker-compose.yml`, plus `.env.example`. Document the homelab endpoint and its secure channel in `docs/observability-architecture.md`.
- [ ] 1.7 Add an OpenTelemetry Collector to the homelab stack (config in `docs/observability-architecture.md`) with an exporter to Tempo, Mimir and Loki. Add a second exporter slot for the LLM tool (section 6).

## 2. LLM Telemetry: Level 1 (self-owned, Grafana)

- [x] 2.1a Custom AI metrics already exist in `Application/Features/Chat/AiMetricsService.cs` (`ai.calls.*`, `ai.tokens.*`, `ai.call.duration`).
- [ ] 2.1 Align `AiMetricsService` (or `IAiCallRecorder` once audit task 5.9 is merged) with the OpenTelemetry GenAI semantic conventions (token usage histogram, operation duration). Check the attribute keys against the current spec version and centralize them in a `GenAiTelemetry` constants class. Add the attributes `app.tenant_id`, `app.pipeline` and `app.fallback`. Keep the old `ai.*` names for one release.
- [ ] 2.2 Enable Semantic Kernel's non-sensitive GenAI diagnostics (`SEMANTICKERNEL_EXPERIMENTAL_GENAI_ENABLE_OTEL_DIAGNOSTICS=true`) and subscribe to the `Microsoft.SemanticKernel*` sources and meters in `ServiceDefaults/Extensions.cs`. Add a test that fails if the `_SENSITIVE` variant is enabled in `appsettings.Production.json` or `docker-compose.yml`.
- [ ] 2.3 Cost estimate: `Ai:Pricing` options (EUR per 1M input and output tokens, per provider and model) and an `app.llm.cost` counter recorded together with the token usage. Unknown models record 0 and log a warning once.
- [ ] 2.4 Server-side time to first token: a histogram in the streaming path (`ChatStreamingService`, or `AiChatService.GetStreamingCompletionAsync` before the split), tagged by pipeline.
- [ ] 2.5 Arena: record per-side latency, tokens and cost with `app.pipeline=arena_alfa|arena_beta`. Store the trace id on `ArenaBattle`, so a vote can be linked to its traces.
- [ ] 2.6 Grafana dashboard "LLM" (JSON committed to `docs/grafana/llm-dashboard.json`): cost per tenant and model, tokens per pipeline, p50/p95 latency and TTFT, fallback rate, and errors by typed code.

## 3. Grafana Faro (Frontend)

- [ ] 3.1 Install `@grafana/faro-web-sdk` and `@grafana/faro-web-tracing` in `src/AsistenteAyuntamiento.Angular`.
- [ ] 3.2 Initialize Faro in `main.ts` from the runtime configuration (not the mutated `environment`, see audit task 7.3), with routing instrumentation enabled.
- [ ] 3.3 Register an Angular `ErrorHandler` that forwards to Faro.
- [ ] 3.4 Limit `propagateTraceHeaderCorsUrls` to the API origin. Verify that browser → API → Worker appears as a single trace.

## 4. TempooAnalytics Setup (Frontend), blocked until the SDK or API is available

- [ ] 4.1 Add the TempooAnalytics client (package or a thin internal HTTP client) in `core/analytics/tempoo-analytics.service.ts`.
- [ ] 4.2 Initialize it with a runtime configuration key, not a build-time `environment` constant.
- [ ] 4.3 Disable any automatic capture of input values, text content or query strings.

## 5. TempooAnalytics UX Telemetry (Frontend)

- [ ] 5.1 Measure the client-side `chat_reaction_time` (send → first chunk) in `ChatStore` (or `chat-panel.ts` before audit task 7.5) and capture it with `{ latency_ms, pipeline }`.
- [ ] 5.2 Track clicks on source citations and external links as `link_clicked` with `{ source_type }` only (BOE/BOJA), never the query or the full URL with parameters.

## 6. LLM Telemetry: Level 2 Spike (self-hosted LLM observability UI)

- [ ] 6.1 Time-boxed spike (≤ 1 day): deploy **Arize Phoenix** locally with Docker, connect it as a second Collector exporter, and check the SK traces (non-sensitive mode), token data and the usefulness of the UI. Write the result in `docs/adr/0002-llm-observability.md`.
- [ ] 6.2 Only if 6.1 shows gaps in prompt versioning or human scores: repeat the spike with **Langfuse** self-hosted (Postgres + ClickHouse + Redis), including sending the Arena votes as scores. Record the resource footprint in the ADR.
- [ ] 6.3 Based on the ADR, either add the chosen tool to the homelab Collector config and the documentation, or close the spike as "Grafana-only is enough".
- [ ] 6.4 Content capture (prompt and completion text) stays **disabled**. Enabling it in any shared environment requires the privacy review described in the design (decision 2) and a separate change.
