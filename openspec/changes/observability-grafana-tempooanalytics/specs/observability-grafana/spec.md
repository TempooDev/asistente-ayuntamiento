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

### Requirement: Token Consumption Metrics
The system SHALL record the number of tokens consumed by the LLM providers to monitor costs and usage patterns.

#### Scenario: Chat completion token tracking
- **WHEN** a chat completion is generated via `AiChatService`
- **THEN** the system SHALL emit an OpenTelemetry metric (Meter) containing the number of prompt tokens, completion tokens, and the model used, which will be visualized in Grafana (Mimir/Prometheus).

### Requirement: Distributed Tracing
The system SHALL propagate OpenTelemetry trace context across all service boundaries and record spans for external dependencies (Postgres, RabbitMQ, S3).

#### Scenario: Inter-service communication
- **WHEN** the Angular frontend calls the .NET API, which in turn publishes a message to RabbitMQ or calls the Go Scraper via gRPC
- **THEN** OpenTelemetry SHALL propagate the W3C `traceparent` and `tracestate` headers to seamlessly link the transaction spans across all involved services in Grafana Tempo.

#### Scenario: Database and Storage Spans
- **WHEN** a service executes a query against PostgreSQL via Entity Framework or downloads a blob from Cloudflare R2
- **THEN** OpenTelemetry instrumentation SHALL automatically record performance spans detailing the query execution time or HTTP latency.
