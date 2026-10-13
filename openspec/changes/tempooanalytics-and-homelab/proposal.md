## Why

Split from `observability-grafana-tempooanalytics` (archived 2026-10-13) to archive the implemented observability work. Two pieces remain:
- **Homelab Collector.** The services, Faro and the configuration are ready, but nothing reaches Grafana until the OpenTelemetry Collector runs in the homelab behind the secure channel.
- **TempooAnalytics.** Product and UX analytics in the frontend: page views, session length, chat reaction time and link clicks. It is blocked until its SDK or API is available.

## What Changes

- Install the OpenTelemetry Collector in the homelab with the configuration in `docs/observability-architecture.md`:
  - fan-out to Tempo, Mimir and Loki;
  - the Faro receiver;
  - bearer token and TLS on the WireGuard/Tailscale tunnel;
  - the content attribute filter.

  Then set `OTEL_EXPORTER_OTLP_*` and `FARO_COLLECTOR_*` in production and load `docs/grafana/llm-dashboard.json`.
- Integrate TempooAnalytics in the Angular frontend with a runtime configuration key and no PII or content.

## Capabilities

### New Capabilities
- `user-experience-tempooanalytics`: product analytics, UX tracking, chat reaction time and engagement metrics in the frontend.

### Modified Capabilities
- (None) The Collector installation completes `observability-grafana` without changing its requirements.

## Impact

- **Infrastructure (homelab):** the Collector, its certificates and token, and the tunnel from the VPS.
- **Frontend (Angular):** a `core/analytics` service and events in `ChatStore` and the citation links.
- **Configuration:** `TEMPOOANALYTICS_API_URL` and `TEMPOOANALYTICS_API_KEY`, plus the production values of the OpenTelemetry and Faro variables.

## Non-goals

- No chat content or PII in TempooAnalytics.
- No new LLM observability tool. ADR 0002 keeps it Grafana-only until content capture is approved.
