# Specification: Scraper Filter Management Service

## Purpose
Provide a unified application service `IScraperFilterService` in `AsistenteAyuntamiento.Application.Features.Scraper` to handle filtering rule queries and modifications for both REST and gRPC consumers.

## Requirements

### Requirement: Centralized Scraper Filter Rule Management
The system SHALL expose an application service to perform CRUD operations on scraper filter rules and retrieve active rules.

#### Scenario: gRPC filter rule lookup
- **WHEN** the Go scraper requests active rules via gRPC `FilterConfigService`
- **THEN** `FilterConfigServiceImpl` SHALL query `IScraperFilterService.GetActiveRulesAsync` without executing direct EF Core queries.

#### Scenario: Admin REST API filter management
- **WHEN** an administrator queries or modifies filter rules via `ScraperFilterEndpoints`
- **THEN** the endpoints SHALL delegate rule persistence and validation to `IScraperFilterService`.
