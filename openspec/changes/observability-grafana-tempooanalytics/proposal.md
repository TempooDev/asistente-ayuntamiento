## Why

Currently, the application lacks visibility into production errors, persistent warnings in user flows, and detailed metrics about user interaction and token consumption. We need to introduce robust observability (using Grafana/OpenTelemetry) and user experience tracing (using TempooAnalytics) to understand how the system performs in the real world, track the chat's reaction time, measure time spent in the app or opening links, and monitor token usage metrics similar to what we have in Aspire.

## What Changes

- Integrate **OpenTelemetry / Grafana Faro** across the stack (Angular Frontend, .NET Backend/Worker, Go Scraper) for error tracking and performance monitoring.
- Enable **Distributed Tracing** via OpenTelemetry to propagate trace IDs across HTTP, gRPC, and RabbitMQ boundaries, allowing full visibility into PostgreSQL and Cloudflare R2 queries.
- Track token consumption metrics using OpenTelemetry Meters (Prometheus).
- Integrate **TempooAnalytics** into the Angular Frontend to capture UX events.
- Implement telemetry for specific UX interactions: chat reaction time, session duration, and link clicks.

## Capabilities

### New Capabilities
- `observability-grafana`: Integration of OpenTelemetry (and Grafana Faro for the frontend) for error tracking, warnings, token consumption metrics, and distributed tracing (across API, gRPC, RabbitMQ, Postgres, and S3). All data is pushed to a homelab Grafana stack (Loki/Tempo/Mimir).
- `user-experience-tempooanalytics`: Integration of TempooAnalytics in the frontend for product analytics, UX tracking, chat reaction times, and user engagement metrics.

### Modified Capabilities
- (None)

## Impact

- **Frontend (Angular)**: Will require adding Grafana Faro Web SDK and TempooAnalytics SDKs (or API calls) and configuring tracking for relevant events.
- **Backend (.NET / Go)**: Will require configuring OpenTelemetry SDKs to export logs (OTLP to Loki), metrics (OTLP to Mimir/Prometheus), and traces (OTLP to Tempo), including custom token consumption metrics.
- **Infrastructure**: New environment variables required for Grafana OTLP endpoints in the homelab and TempooAnalytics API keys.

## Non-goals

- We will not rely on managed SaaS solutions (like Sentry or Datadog) for observability, maintaining full control via the homelab Grafana stack.
- We will not track sensitive user PII or chat content in TempooAnalytics; telemetry will focus purely on performance and UX metrics.
