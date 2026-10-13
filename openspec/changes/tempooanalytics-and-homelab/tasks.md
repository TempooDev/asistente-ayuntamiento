> Split from `observability-grafana-tempooanalytics` on 2026-10-13. Section 2 is blocked until the TempooAnalytics SDK or API is available.

## 1. Homelab Collector

- [ ] 1.1 Install `otelcol-contrib` in the homelab with the configuration in `docs/observability-architecture.md`: OTLP and Faro receivers on the tunnel address with TLS and `bearertokenauth`, the `attributes/drop-content` processor, and exporters to Tempo, Mimir (OTLP, default translation) and Loki. (Was task 1.7.)
- [ ] 1.2 Set the tunnel between the VPS and the homelab (WireGuard or Tailscale), then `OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS` and `FARO_COLLECTOR_URL`/`FARO_COLLECTOR_TOKEN` in production.
- [ ] 1.3 Import `docs/grafana/llm-dashboard.json` and check the panels with real data, including a single browser → API → Worker trace in Tempo (pending from task 3.4) and the cost with `AI_CHAT_PRICE_*_EUR` set.

## 2. TempooAnalytics Setup (Frontend), blocked until the SDK or API is available

- [ ] 2.1 Add the TempooAnalytics client (package or a thin internal HTTP client) in `core/analytics/tempoo-analytics.service.ts`.
- [ ] 2.2 Initialize it with a runtime configuration key, not a build-time `environment` constant (same pattern as `/telemetry/config`).
- [ ] 2.3 Disable any automatic capture of input values, text content or query strings.

## 3. TempooAnalytics UX Telemetry (Frontend)

- [ ] 3.1 Measure the client-side `chat_reaction_time` (send → first chunk) in `ChatStore` and capture it with `{ latency_ms, pipeline }`.
- [ ] 3.2 Track clicks on source citations and external links as `link_clicked` with `{ source_type }` only (BOE/BOJA), never the query or the full URL with parameters.
