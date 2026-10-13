# observability-grafana Specification

## Purpose
Send errors, traces, metrics and logs of every service (Angular with Grafana Faro, .NET ApiService/Worker/Gateway, Go scraper) to a self-hosted Grafana stack through OpenTelemetry, with end-to-end traces across HTTP, gRPC and RabbitMQ, and LLM usage, cost and latency metrics that never carry prompt or completion text. See `docs/observability-architecture.md` and `docs/adr/0002-llm-observability.md`.

## Requirements
### Requirement: Telemetry Export per Service
Each service SHALL export its telemetry over OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set:
- .NET services (`AddServiceDefaults`) SHALL export traces, metrics and logs (formatted messages and scopes), including the `AsistenteAyuntamiento.*` and `Microsoft.SemanticKernel*` sources and meters; `/health` and `/alive` requests SHALL NOT be traced.
- The Go scraper SHALL export traces and metrics; its logs SHALL be `slog` JSON on stdout carrying `trace_id` and `span_id` when a span is active.

#### Scenario: Backend unhandled exception
- **WHEN** an ApiService endpoint throws an unhandled exception
- **THEN** the exception SHALL be logged through the OpenTelemetry logger with the trace context of the request
- **AND** the request span SHALL record `http.response.status_code` 500

#### Scenario: Collector not configured
- **WHEN** `OTEL_EXPORTER_OTLP_ENDPOINT` is empty
- **THEN** the services SHALL start normally without exporting telemetry

### Requirement: Frontend Error Tracking with Faro
The SPA SHALL report errors to Grafana Faro through Angular's `ErrorHandler` (`FaroErrorHandler`, which keeps the default console logging). Faro's own error and console instrumentations SHALL be off; the session, view, web-vitals and tracing instrumentations SHALL be on. Faro SHALL be loaded lazily after startup and only when the gateway reports a collector (`GET /telemetry/config` with `enabled=true`); the SPA SHALL send data to the gateway's same-origin `/telemetry/faro`, which forwards it to the collector with the collector token, without cookies or the user's `Authorization` header, rate limited per client IP and capped at 256 KB per request. Page URLs SHALL be sent without query string or fragment, and views SHALL be named by route template.

#### Scenario: Frontend unhandled error
- **GIVEN** the gateway has `Telemetry:Faro:CollectorUrl` configured
- **WHEN** the Angular application raises an unhandled error
- **THEN** `FaroErrorHandler` SHALL push it to Faro exactly once
- **AND** errors raised while Faro is still loading SHALL be sent once it starts (up to 20)

#### Scenario: Faro disabled
- **WHEN** `Telemetry:Faro:CollectorUrl` is empty
- **THEN** the SPA SHALL NOT load the Faro SDK and SHALL discard queued errors

### Requirement: LLM Usage, Cost and Latency Metrics
The system SHALL record, for every LLM call, metrics on the meter `AsistenteAyuntamiento.Ai` following the GenAI conventions: `gen_ai.client.operation.duration`, `gen_ai.client.inference.usage.input_tokens` and `gen_ai.client.inference.usage.output_tokens`, tagged with `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `app.tenant_id`, `app.pipeline`, `app.fallback` and `app.llm.operation` (`answer`, `query_expansion`, `enrichment`, `history_analysis`). It SHALL also record `app.llm.time_to_first_token` for streamed responses and `app.llm.cost`, an estimate in EUR from `Ai:Pricing:{provider}:{model}:{Input,Output}` (per million tokens). User and session ids SHALL NOT be metric labels.

#### Scenario: Chat completion token tracking
- **WHEN** a chat completion is generated for tenant `org_A` with the hierarchical pipeline
- **THEN** the system SHALL emit duration, input and output tokens and estimated cost tagged with the provider, the model, `app.tenant_id=org_A`, `app.pipeline=hierarchical` and `app.llm.operation=answer`

#### Scenario: Fallback provider used
- **WHEN** a streamed call is served by the platform fallback model after a quota error
- **THEN** the metrics SHALL carry `app.fallback=true` and the fallback model name

#### Scenario: Model missing from the price table
- **WHEN** a call to a non-Ollama model without a configured price reports tokens
- **THEN** the cost recorded SHALL be 0 and a warning SHALL be logged once per provider and model

#### Scenario: Self-hosted model
- **WHEN** a call uses the `ollama` provider
- **THEN** the cost recorded SHALL be 0 without a warning

#### Scenario: No token usage reported
- **WHEN** a call reports 0 input and 0 output tokens
- **THEN** the duration SHALL be recorded but no token counts and no cost

### Requirement: LLM Traces Without Content
Semantic Kernel's GenAI diagnostics SHALL be enabled without prompt or completion text (`Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnostics`). The sensitive switch SHALL NOT be enabled anywhere; `TelemetryPrivacyTests` SHALL fail if `EnableOTelDiagnosticsSensitive`, `SEMANTICKERNEL_EXPERIMENTAL_GENAI_ENABLE_OTEL_DIAGNOSTICS_SENSITIVE` or `EnableSensitiveData` appears in a `docker-compose*.yml`, `.env.example`, any `appsettings*.json` or any `.cs` file under `src`.

#### Scenario: Default configuration
- **WHEN** the services run with the committed configuration
- **THEN** LLM spans SHALL contain the model, operation and token usage, but no prompt or completion text

#### Scenario: Sensitive diagnostics enabled by mistake
- **WHEN** the sensitive Semantic Kernel switch is added to a compose file, settings file or source file
- **THEN** the architecture test suite SHALL fail

### Requirement: Distributed Tracing
The system SHALL propagate W3C trace context (`traceparent`, `tracestate`) across service boundaries:
- the SPA (through Faro tracing, when enabled) on requests to the API's `/api/` routes;
- the ApiService gRPC client to the Go scraper (`otelgrpc` server handler) and the Go scraper gRPC client to the ApiService;
- AMQP message headers published by the Go scraper and the .NET publishers, continued by the ingestion consumers built on `RabbitMqConsumerBase` (`RabbitMqTraceContext`).

Dependency spans SHALL be recorded for incoming ASP.NET Core requests, outgoing HTTP and gRPC calls, and the Aspire client integrations (PostgreSQL through Npgsql, RabbitMQ).

#### Scenario: Go scraper publishes to RabbitMQ
- **GIVEN** telemetry export is configured in the ApiService, the Go scraper and the Worker
- **WHEN** the Go scraper publishes an ingestion message during a scrape triggered by the ApiService through gRPC
- **THEN** the AMQP headers SHALL contain `traceparent`
- **AND** the Worker span that processes the message SHALL belong to the same trace as the gRPC call

#### Scenario: Browser call to the API
- **GIVEN** Faro is enabled and the session is sampled
- **WHEN** the SPA calls `/api/users/me`
- **THEN** the request SHALL carry `traceparent` and the ApiService span SHALL continue the browser trace
