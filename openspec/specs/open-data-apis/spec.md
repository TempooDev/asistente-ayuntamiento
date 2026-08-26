# Specification: BOJA and BOPMA Open Data API Integration

## Purpose
Replace the fragile HTML/RSS scraping (BOJA) and raw PDF parsing (BOPMA) with robust integrations using the official Open Data APIs (JSON) provided by the Junta de Andalucía and Diputación de Málaga. This improves reliability, extraction speed, and prevents format-breaking errors when the official portals update their UI.

## Requirements

### Requirement: Provider Selection Strategy
The Go scraper SHALL support multiple strategies for fetching a bulletin, allowing the system to use the Open Data API by default but fallback or be configured to use the legacy HTML/PDF methods.

#### Scenario: Configuring the active provider
- **WHEN** the `go-scraper` initializes
- **THEN** it SHALL read the environment variables `BOJA_STRATEGY` and `BOPMA_STRATEGY`
- **AND** if set to `API`, it SHALL inject the `ApiProvider` implementations
- **AND** if set to `LEGACY` or empty, it SHALL inject the legacy providers to maintain backward compatibility.

### Requirement: BOJA API Client
The system SHALL fetch BOJA dispositions using the Junta de Andalucía Open Data API.

#### Scenario: Fetching BOJA summary via JSON
- **WHEN** the scraper requests the daily summary for BOJA
- **THEN** the `ApiProvider` SHALL query the official BOJA JSON endpoints
- **AND** parse the JSON array to extract document IDs/URLs instead of parsing the XML RSS feeds (`s51.xml`, etc.).

#### Scenario: Fetching BOJA document content
- **WHEN** the scraper fetches a specific BOJA document
- **THEN** the `ApiProvider` SHALL retrieve the plain text from the API payload
- **AND** map the official metadata fields (Title, Section, Publication Date) directly to the `scraper.Document` struct, completely bypassing `StripHTMLTags()`.

### Requirement: BOPMA API Client
The system SHALL fetch BOPMA edicts using the Diputación de Málaga Open Data API.

#### Scenario: Fetching BOPMA edicts via JSON
- **WHEN** the scraper requests the daily summary for BOPMA
- **THEN** the `ApiProvider` SHALL query the BOPMA Edicts dataset JSON endpoint
- **AND** extract the summary, metadata, and edict content directly from the JSON structure
- **AND** completely avoid downloading and parsing binary PDF files (`ledongthuc/pdf`), ensuring a 100% success rate in text extraction.

### Requirement: Telemetry and Rate Limiting
The new API providers SHALL inherit the observability and throttling rules of the system.

#### Scenario: Tracing API calls
- **WHEN** the `ApiProvider` makes an HTTP request
- **THEN** the request SHALL be instrumented using OpenTelemetry (`otelhttp`)
- **AND** the provider SHALL respect the 1 request/second rate limiter configured for both BOJA and BOPMA.
