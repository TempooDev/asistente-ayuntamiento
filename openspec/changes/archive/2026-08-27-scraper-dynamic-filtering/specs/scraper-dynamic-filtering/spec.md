## ADDED Requirements

### Requirement: Database-Driven Filtering Configuration
The .NET backend SHALL persist scraping filter rules in the PostgreSQL database using Entity Framework Core.

#### Scenario: Defining Filter Entities
- **WHEN** the system initializes
- **THEN** it SHALL include a `ScraperFilterRule` entity with properties such as: `Id`, `Provider` (BOE/BOJA/BOPMA), `FilterType` (Department, Section, Keyword), `Value` (e.g., "Ministerio de Hacienda"), and `IsActive`.

#### Scenario: API Management of Filters
- **WHEN** an administrator uses the Angular UI
- **THEN** they SHALL be able to perform CRUD operations on `ScraperFilterRule` via protected HTTP endpoints in the .NET API.

### Requirement: gRPC Microservices Communication
The system SHALL use gRPC (Protocol Buffers over HTTP/2) for direct, strongly-typed, and highly efficient communication between the Go scraper and the .NET API.

#### Scenario: Defining the Protobuf Contracts
- **WHEN** implementing the communication
- **THEN** a shared `.proto` file SHALL define two services:
  1. `FilterConfigService` (hosted by .NET) to serve active filters.
  2. `ScraperCommandService` (hosted by Go) to receive administration commands.

### Requirement: Go Scraper Dynamic Configuration (gRPC Client)
The Go scraper SHALL retrieve its filtering configuration from the .NET backend using gRPC.

#### Scenario: Retrieving rules before scraping
- **WHEN** the Go scraper initiates a scheduled or manual scrape
- **THEN** it SHALL act as a gRPC client and call `GetActiveFilters()` on the .NET API.
- **AND** it SHALL apply the returned rules to evaluate metadata provided by the Open Data APIs (BOE/BOJA/BOPMA).
- **AND** documents failing the filter SHALL be discarded in-memory and NOT uploaded to S3.

### Requirement: On-Demand Scrape (gRPC Server on Go)
The system SHALL support forcing a specific scrape job synchronously from the UI using gRPC.

#### Scenario: Triggering a Scrape Command
- **GIVEN** an administrator wants to scrape a specific section of BOJA immediately
- **WHEN** they trigger the action in the Angular UI
- **THEN** the .NET API SHALL act as a gRPC client and call `ForceScrape()` on the Go scraper's gRPC server.
- **AND** the Go scraper SHALL execute the targeted Open Data API extraction synchronously, upload to S3, and return a typed gRPC response with the execution summary to .NET.
