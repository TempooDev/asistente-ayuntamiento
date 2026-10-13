# Specification: Observability with Grafana & OpenTelemetry

## Purpose
Track system errors, unhandled exceptions, persistent warnings, and token usage metrics across the Angular SPA, ASP.NET Core APIs, .NET background worker, and Go scraper using a self-hosted Grafana stack (Loki/Tempo/Mimir).

## ADDED Requirements

### Requirement: Global Error Tracking
The system SHALL capture unhandled exceptions from all services and report them via OpenTelemetry (OTLP) to Grafana (Loki/Tempo).

#### Scenario: Backend unhandled exception
- **WHEN** the ASP.NET Core API or Go scraper throws an unhandled exception
- **THEN** it SHALL be reported to the OpenTelemetry collector with the stack trace, HTTP request context (if applicable), and environment tags.

#### Scenario: Frontend unhandled error
- **WHEN** the Angular application encounters an unhandled JavaScript error
- **THEN** the Grafana Faro Web SDK SHALL report it automatically with breadcrumbs and browser context.

### Requirement: LLM Usage, Cost and Latency Metrics
The system SHALL record, for every LLM call, OpenTelemetry metrics that follow the GenAI semantic conventions (token usage by type, operation duration), enriched with `app.tenant_id`, `app.pipeline` and `app.fallback`. It SHALL also record an estimated cost from a configured price table, and the server-side time to first token for streamed responses. All data SHALL stay on self-hosted infrastructure.

#### Scenario: Chat completion token tracking
- **WHEN** a chat completion is generated for tenant `org_A` with the hierarchical pipeline
- **THEN** the system SHALL emit token usage (input and output), duration and estimated cost, tagged with the provider, the model, `app.tenant_id=org_A` and `app.pipeline=hierarchical`
- **AND** Grafana SHALL be able to chart the cost per tenant and per model

#### Scenario: Fallback provider used
- **WHEN** a call is served by the fallback provider
- **THEN** the metrics SHALL carry `app.fallback=true` and the fallback model name

#### Scenario: Model missing from the price table
- **WHEN** a call uses a model without a configured price
- **THEN** the cost recorded SHALL be 0 and a warning SHALL be logged once per model

### Requirement: LLM Traces Without Content by Default
The system SHALL emit traces for LLM operations (retrieval, completion, streaming) without prompt or completion text. Content capture SHALL be disabled in production configuration unless a separate, privacy-approved change enables it.

#### Scenario: Default production configuration
- **WHEN** the services run with the production configuration
- **THEN** LLM spans SHALL contain the model, operation and token usage, but no prompt or completion text

#### Scenario: Sensitive diagnostics enabled by mistake
- **WHEN** the sensitive Semantic Kernel diagnostics switch is present in the production settings or compose files
- **THEN** the configuration test suite SHALL fail

#### Scenario: Optional LLM observability backend
- **WHEN** a self-hosted LLM observability tool (Phoenix or Langfuse) is adopted after the spike
- **THEN** it SHALL receive the same OTLP data through the OpenTelemetry Collector
- **AND** no service code SHALL depend on that tool's SDK

### Requirement: Distributed Tracing
The system SHALL propagate OpenTelemetry trace context across all service boundaries and record spans for external dependencies (Postgres, RabbitMQ, S3).

#### Scenario: Inter-service communication
- **WHEN** the Angular frontend calls the .NET API, which in turn publishes a message to RabbitMQ or calls the Go Scraper via gRPC
- **THEN** OpenTelemetry SHALL propagate the W3C `traceparent` and `tracestate` headers to seamlessly link the transaction spans across all involved services in Grafana Tempo.

#### Scenario: Go scraper publishes to RabbitMQ
- **WHEN** the Go scraper publishes an ingestion message during a job triggered by the .NET API through gRPC
- **THEN** the AMQP message headers SHALL contain `traceparent`
- **AND** the .NET Worker span that processes the message SHALL belong to the same trace as the gRPC call

#### Scenario: Database and Storage Spans
- **WHEN** a service executes a query against PostgreSQL via Entity Framework or downloads a blob from Cloudflare R2
- **THEN** OpenTelemetry instrumentation SHALL automatically record performance spans detailing the query execution time or HTTP latency.
