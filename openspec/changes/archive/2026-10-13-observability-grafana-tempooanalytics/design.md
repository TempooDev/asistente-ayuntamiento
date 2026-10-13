> **Revised 2026-10-13.** The previous version of this design described Sentry, which contradicted the proposal and specs (self-hosted Grafana and OpenTelemetry). It is rewritten here to match the proposal, the current code, the LLM-specific observability decision, and the ordering with `audit-remediation-2026-10`.

## Context

Observability that already exists in the code:
- **.NET:** `AsistenteAyuntamiento.ServiceDefaults/Extensions.cs` configures OpenTelemetry (ASP.NET Core and HttpClient instrumentation, runtime metrics, and an OTLP exporter when `OTEL_EXPORTER_OTLP_ENDPOINT` is set). The Aspire client integrations (Npgsql EF Core, RabbitMQ) add their own tracing sources. gRPC client instrumentation is commented out (`Extensions.cs:70-71`).
- **AI metrics:** `Application/Features/Chat/AiMetricsService.cs` emits custom metrics (`ai.calls.*`, `ai.call.duration`, `ai.tokens.input|output|total`) and an in-memory snapshot used by `/api/ai/metrics`. These names are not the OpenTelemetry GenAI conventions, and the metrics carry no tenant, pipeline or cost attributes.
- **Go:** `internal/telemetry/otel.go` configures traces, metrics and W3C propagation over OTLP. Providers create spans. The gRPC server and client have no `otelgrpc` handler, and RabbitMQ publishing does not inject `traceparent`.
- **Missing:**
  - `OTEL_EXPORTER_OTLP_ENDPOINT` is not set in `docker-compose.yml`, so production exports nothing.
  - Grafana Faro and TempooAnalytics are not implemented in Angular.

## Goals / Non-Goals

**Goals:**
- Production traces, metrics and logs in the homelab Grafana stack (Tempo, Mimir, Loki) for the API, Worker and Go scraper, with end-to-end traces across HTTP, gRPC and RabbitMQ.
- **LLM-specific observability on our own infrastructure:**
  - token usage, cost estimate, latency and time to first token per tenant, model, provider and pipeline;
  - LLM call traces (retrieval → prompt → completion);
  - a link between the Arena human votes and the traces.
- Frontend errors (Faro) and UX events (TempooAnalytics), without PII.

**Non-Goals:**
- No managed SaaS (Sentry, Datadog, LangSmith or similar).
- No prompt or completion content in telemetry by default. Citizens' questions may contain personal data.
- No automated LLM evaluation (LLM-as-judge) in this change. The chosen tool should make it possible later.

## Decisions

### 1. OpenTelemetry as the only instrumentation API
- **Decision:** every service exports OTLP. Grafana (Tempo, Mimir, Loki) is the main backend. Any LLM-specific tool must accept OTLP, so the instrumentation is not tied to a vendor.
- **Rationale:** it is already in place in ServiceDefaults and Go, and it keeps backend choices reversible.

### 2. LLM telemetry: standard GenAI signals first, a self-hosted LLM tool as a second layer

**Level 1 (mandatory, no new infrastructure)**
- Enable Semantic Kernel's **non-sensitive** GenAI diagnostics: the `Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnostics` switch, or `SEMANTICKERNEL_EXPERIMENTAL_GENAI_ENABLE_OTEL_DIAGNOSTICS=true`.
- Subscribe to the `Microsoft.SemanticKernel*` tracing sources and meters in ServiceDefaults. This emits spans and metrics for model calls with model name, operation and token usage, and no prompt text.
- Align `AiMetricsService` with the OpenTelemetry **GenAI semantic conventions**: token usage histogram, operation duration, and the attributes for system, model and token type. Add our own attributes: `app.tenant_id`, `app.pipeline` (`baseline` | `hierarchical` | `arena_alfa` | `arena_beta` | `shadow`), and `app.fallback` (bool).
  - The exact attribute keys are checked against the current spec version during task 2.1, because the GenAI conventions are still evolving.
  - The old `ai.*` names stay for one release, so existing panels do not break, and are then removed.
- **Cost estimate:** a price table in configuration (`Ai:Pricing:{provider}:{model}:{input,output}` per 1M tokens) feeds an `app.llm.cost` counter (EUR). No external pricing API.
- **Time to first token:** a histogram measured on the server, plus the client-side value reported to TempooAnalytics (spec `user-experience-tempooanalytics`).
- **Grafana dashboard:** "LLM" with cost per tenant and model, tokens per pipeline, p50/p95 latency and TTFT, fallback rate, and error rate by typed code (`LLM_QUOTA_EXCEEDED`, etc., from `audit-remediation-2026-10`).

**Level 2 (optional, decided by a spike): a self-hosted LLM observability UI**

| Option | Licence | Footprint | Strengths for this project | Concerns |
|---|---|---|---|---|
| **Langfuse** (self-hosted) | MIT core (enterprise modules separate) | Postgres + ClickHouse + Redis (+ S3) | Traces, prompt management, datasets, scores and annotation queues. Arena votes can be stored as scores on traces. | Heaviest stack to run in the homelab |
| **Arize Phoenix** (self-hosted) | Elastic License 2.0 | Single container (+ Postgres) | OTLP- and OpenInference-native, lighter, good trace and eval UI | ELv2 restricts offering it as a managed service (not our case) |
| **Grafana only** (level 1) | n/a | None | Already planned, no new service | No prompt/version view, no datasets or scores |

- **Default recommendation:** start with level 1. Run a time-boxed spike (task 6.1) with **Phoenix first**, because it is the lightest to run next to the existing stack. Use Langfuse only if prompt versioning and human-score workflows (Arena) justify its footprint.
- Both receive the same OTLP data through an OpenTelemetry Collector **fan-out**: one pipeline to Grafana, one to the LLM tool. The services do not change.

**Content capture**
- Off everywhere by default.
- In the LLM tool only, prompt and completion content may be enabled per environment with the `_SENSITIVE` switch, **only** after a DPO/privacy review (to be agreed). The conditions are: retention ≤ 30 days, access restricted to platform admins, and anonymization of the user question where possible.
- In production this stays disabled until that review is done.

### 3. Trace propagation across RabbitMQ and gRPC
- **.NET:** RabbitMQ.Client 7 / Aspire propagate context on the .NET side. Verify this (task 1.2). The Go publisher injects `traceparent` and `tracestate` into the AMQP headers, as defined in `docs/messaging-contract.md` (`audit-remediation-2026-10` task 3.1).
- **gRPC:** enable the client instrumentation in ServiceDefaults, and add the `otelgrpc` stats handlers to the Go server (`commandserver`) and client (`filterclient`).

### 4. Frontend
- **Grafana Faro Web SDK:** errors, web vitals, and routing. `propagateTraceHeaderCorsUrls` is limited to the API origin.
- **TempooAnalytics:** in-house UX analytics (`chat_reaction_time`, `link_clicked`, sessions). It is blocked until its SDK or API is available.

### 5. Ordering with `audit-remediation-2026-10`
- Implement after **phase 3** of the audit. The messaging contract, the typed LLM error codes and the extraction of `IAiCallRecorder` (audit task 5.9) are the natural insertion points.
- If audit 5.9 is not merged yet, task 2.x instruments `AiMetricsService` directly, and the code moves with the refactor.

## Risks / Trade-offs

- **Prompt content leaking into telemetry** → content capture off by default. A configuration test fails if the `_SENSITIVE` switch is enabled in production settings.
- **High-cardinality labels in Mimir** (`tenant_id`, `model`) → the number of tenants and models is small (tens). Session IDs and user IDs are never used as metric labels.
- **Duplicate spans** if both SK diagnostics and `Microsoft.Extensions.AI` `UseOpenTelemetry()` are enabled → enable only the SK switch, and verify in the spike.
- **Homelab load** (ClickHouse for Langfuse) → that is why Phoenix and Grafana-only come first.
- **The GenAI conventions are still evolving** → the attribute keys are centralized in a single `GenAiTelemetry` constants class.

## Open Questions
- Is the homelab Grafana stack (Tempo, Mimir, Loki, plus a Collector) reachable from the VPS, and over which secured channel (VPN, mTLS)?
- What is the availability of the TempooAnalytics SDK or API?
- Who acts as privacy owner to approve any content capture?
