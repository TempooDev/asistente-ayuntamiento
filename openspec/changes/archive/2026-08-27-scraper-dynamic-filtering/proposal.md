## Why

The system currently relies on static/hardcoded scraping rules for extracting data from official gazettes (BOE, BOJA, BOPMA). This requires code changes and deployments to adjust what data is scraped. We need a dynamic, database-driven filtering configuration that allows system administrators to control scraping rules directly from the Angular UI. Furthermore, we need instantaneous configuration fetching and on-demand scraping triggers between the .NET backend and the Go scraper, which can be efficiently solved using gRPC.

## What Changes

- Create a `ScraperFilterRule` entity to persist scraping filter rules in PostgreSQL via Entity Framework Core.
- Add protected CRUD API endpoints in the .NET backend for managing filter rules.
- Add a shared protobuf contract for `FilterConfigService` (served by .NET) and `ScraperCommandService` (served by Go).
- Update the Go scraper to act as a gRPC client, fetching rules from the .NET backend and applying them in-memory to discard unwanted documents.
- Add an on-demand gRPC scraping trigger from the .NET backend to the Go scraper for synchronous targeted extraction.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `scraper-dynamic-filtering`: Add dynamic database-driven filtering configuration and gRPC-based microservice communication for fetching rules and triggering on-demand scrapes.

## Impact

- **Database**: New table for `ScraperFilterRule`.
- **.NET API**: New CRUD endpoints and `FilterConfigService` gRPC server. New `ScraperCommandService` gRPC client.
- **Go Scraper**: New `FilterConfigService` gRPC client. New `ScraperCommandService` gRPC server.

## Non-goals
- Full rewrite of the Go scraper logic beyond the filtering and trigger mechanisms.
- Changing the underlying Open Data API sources (BOE, BOJA, BOPMA).
