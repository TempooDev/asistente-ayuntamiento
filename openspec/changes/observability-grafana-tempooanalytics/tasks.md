## 1. OpenTelemetry Setup - Backend

- [ ] 1.1 Add `OpenTelemetry`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, and `OpenTelemetry.Instrumentation.AspNetCore` to `AsistenteAyuntamiento.ApiService`. Configure tracing and EF Core integration in `Program.cs`.
- [ ] 1.2 Add OpenTelemetry instrumentation to `AsistenteAyuntamiento.Worker` and configure tracing (including RabbitMQ message headers extraction) in `Program.cs`.
- [ ] 1.3 Add OpenTelemetry Go SDK (`go.opentelemetry.io/otel`) to `go-scraper`. Configure the gRPC server interceptors in `commandserver` to extract W3C trace headers.
- [ ] 1.4 Add `OTEL_EXPORTER_OTLP_ENDPOINT` to `docker-compose.yml` for `apiservice`, `worker`, and `go-scraper`.

## 2. Token Consumption Metrics - Backend

- [ ] 2.1 Modify `AiChatService.cs` in `AsistenteAyuntamiento.ApiService` to emit a custom OpenTelemetry metric (Counter/Histogram) containing token usage (prompt, completion, model) after each chat request.

## 3. Grafana Faro Setup - Frontend

- [ ] 3.1 Install `@grafana/faro-web-sdk` and `@grafana/faro-web-tracing` in `AsistenteAyuntamiento.Angular`.
- [ ] 3.2 Configure Faro SDK initialization in `main.ts` using the environment configuration. Enable routing instrumentation.
- [ ] 3.3 Ensure the Angular `ErrorHandler` is registered to forward unhandled errors to Faro.
- [ ] 3.4 Enable `propagateTraceHeaderCorsUrls` in Faro so W3C trace headers are injected into outbound HTTP calls to the `.NET API`.

## 4. TempooAnalytics Setup - Frontend

- [ ] 4.1 Install the TempooAnalytics tracking package (or prepare the internal API client) in `AsistenteAyuntamiento.Angular`.
- [ ] 4.2 Initialize TempooAnalytics in `app.component.ts` (or `main.ts`) leveraging a new environment variable `TEMPOOANALYTICS_API_KEY`.
- [ ] 4.3 Configure TempooAnalytics initialization options to avoid capturing sensitive data automatically.

## 5. TempooAnalytics UX Telemetry - Frontend

- [ ] 5.1 Update `AiChatService` (Angular side) or `chat-panel.ts` to measure `chat_reaction_time` (TTFT) and call `tempooAnalytics.capture('chat_reaction_time', { latency_ms: X })`.
- [ ] 5.2 Add `(click)` tracking to source citations and external links in the chat UI, capturing `link_clicked` with the destination URL/type via `tempooAnalytics.capture`.

