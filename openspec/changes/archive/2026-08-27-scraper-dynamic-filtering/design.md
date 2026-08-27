## Context

The Go scraper currently relies on hardcoded configurations for extracting documents from BOE, BOJA, and BOPMA. To allow administrative flexibility, we are introducing database-driven filter rules that the scraper will dynamically fetch and apply. The Angular frontend will manage these rules through the .NET backend. Because the Go scraper is an independent service, it needs a fast and strongly-typed way to retrieve rules and receive manual commands (like triggering a scrape on-demand). Using gRPC is an ideal fit, as both Go and .NET have mature support.

## Goals / Non-Goals

**Goals:**
- Store and manage scraping rules (`ScraperFilterRule`) via Entity Framework Core.
- Introduce a bidirectional gRPC architecture: .NET serves rules to Go, and Go receives commands from .NET.
- Update the Go scraper to fetch the rules before initiating an extraction run.

**Non-Goals:**
- Completely rewriting the Go scraper architecture.
- Extending filtering capabilities beyond what the Open Data APIs support (it only drops non-matching files post-fetch/metadata extraction).

## Decisions

1. **Protocol: gRPC for Microservices Communication**
   *Rationale:* High performance and strong typing. It provides clear `.proto` contracts.
   *Alternative considered:* REST HTTP APIs. Rejected because gRPC requires less boilerplate for client generation and is faster for internal communication.

2. **Entity Framework Core for Storage**
   *Rationale:* Follows existing .NET backend conventions. The new `ScraperFilterRule` will reside in PostgreSQL.

3. **In-Memory Filtering in Go**
   *Rationale:* The Go scraper will fetch all rules periodically or right before a run. Since rules apply to the metadata of the files to be extracted, the scraper evaluates each item against the active rules and discards mismatches before downloading or uploading the PDF to S3.

4. **Service Hosting**
   *Rationale:*
   - `.NET ApiService` will host the gRPC server for `FilterConfigService`.
   - `Go Scraper` will host the gRPC server for `ScraperCommandService`.

## Risks / Trade-offs

- **Risk:** The Go scraper fails to connect to the .NET gRPC server.
  **Mitigation:** Implement retry logic in Go. If it still fails, the scraper should either abort the run or rely on previously cached rules.

- **Risk:** gRPC bidirectional dependency makes local development slightly more complex.
  **Mitigation:** Use Aspire to orchestrate and inject correct endpoints between the .NET and Go services seamlessly.
