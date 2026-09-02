## Context

Currently, the system lacks centralized error reporting and UX metrics, making it difficult to debug issues in production or understand how users interact with the Angular frontend. The backend consists of ASP.NET Core APIs, .NET background workers, and a Go scraper, while the frontend is Angular. We need a unified approach to observability (Sentry) and a dedicated UX analytics tool (TempooAnalytics).

## Goals / Non-Goals

**Goals:**
- Implement Sentry across the stack (Angular, ASP.NET Core, .NET Worker, Go Scraper) to capture unhandled exceptions and persistent warnings.
- Track custom metrics (e.g., token consumption) via Sentry.
- Implement TempooAnalytics in the Angular SPA to track UX metrics: chat reaction times, session durations, and user flow metrics (e.g., clicking on links).

**Non-Goals:**
- We will not send any personally identifiable information (PII), such as exact chat queries, documents content, or passwords, to Sentry or TempooAnalytics.
- We are not replacing our local logs or Aspire dashboard in development, only adding production observability.

## Decisions

1. **Sentry for Error Tracking and System Metrics:**
   - *Why:* Sentry has robust SDKs for Angular, .NET Core, and Go. It provides unified error tracking and can also capture custom telemetry (such as tokens consumed by the LLM).
   - *Alternative considered:* Application Insights, Datadog. Sentry is more developer-friendly for this stack and has a generous free tier.
2. **TempooAnalytics for UX Analytics:**
   - *Why:* TempooAnalytics is our custom in-house tracking service, allowing us full control over data sovereignty and avoiding third-party vendor lock-in for sensitive UX data.
   - *Alternative considered:* PostHog, Google Analytics. While PostHog offers auto-capture, bringing the data into TempooAnalytics aligns better with our internal ecosystem.
3. **Distributed Tracing (Sentry Performance):**
   - *Why:* The system communicates across multiple boundaries (Angular -> API -> RabbitMQ -> Worker -> Postgres/R2) and (API -> gRPC -> Go Scraper). A failure or bottleneck in RabbitMQ or Postgres affects the user experience. By enabling Sentry's Distributed Tracing (passing `sentry-trace` and `baggage` headers) and instrumenting RabbitMQ/Postgres/S3, we can visualize the entire journey of a request across all microservices and external resources.

## Risks / Trade-offs

- **Risk:** Sensitive data leaking to third-party services.
  - *Mitigation:* Explicitly configure data scrubbing in both Sentry and TempooAnalytics. Avoid logging the payload of chat requests.
- **Risk:** Performance overhead from SDKs.
  - *Mitigation:* Ensure TempooAnalytics tracking calls are asynchronous and non-blocking. Configure Sentry's trace sample rate appropriately (e.g., 10-20% in production).
