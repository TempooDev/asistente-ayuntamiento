# Specification: Go Scraper Worker (BOE, BOJA, BOPMA)

## Purpose
Provide a Go-based scraper service (Echo + Air) that daily fetches official gazette documents from BOE, BOJA, and BOPMA, uploads each PDF to Cloudflare R2, and publishes an ingestion event to RabbitMQ for the .NET worker to process.

---

## Source Analysis

### BOE — Official Open Data REST API
**Approach:** REST API (`GET /datosabiertos/api/boe/sumario/{YYYYMMDD}`) returns JSON with all daily items including direct PDF URLs. No authentication, no anti-scraping protection.

**URL Pattern:**
- Sumario: `https://www.boe.es/datosabiertos/api/boe/sumario/20260715`
- PDF: `https://www.boe.es/boe/dias/{year}/{month}/{day}/pdfs/{id}.pdf`

**Complexity:** ✅ Low — pure HTTP, JSON parsing, direct PDF download.

---

### BOJA — Official Atom/RSS Feed per Section
**Approach:** Parse the official Atom feeds published per gazette section (`/boja/distribucion/s{N}.xml`). Each `<entry>` contains a direct PDF URL. No authentication.

**Feed URLs (section-based):**
- `https://www.juntadeandalucia.es/boja/distribucion/s51.xml` (Disposiciones Generales)
- `https://www.juntadeandalucia.es/boja/distribucion/s52.xml` (Autoridades y Personal)
- `https://www.juntadeandalucia.es/boja/distribucion/s53.xml` (Otras Disposiciones)
- `https://www.juntadeandalucia.es/boja/distribucion/s54.xml` (Administración de Justicia)
- `https://www.juntadeandalucia.es/boja/distribucion/s55.xml` (Anuncios)

**PDF URL Pattern:**
`http://www.juntadeandalucia.es/boja/{year}/{num_boja}/BOJA{yy}-{num}-{xxx}-{id}.pdf`

**Complexity:** ✅ Low-Medium — XML Atom parsing, direct PDF download, multiple feeds.

---

### BOPMA — Cloudflare Turnstile Protected
**Approach:** UNCERTAIN — requires investigation spike before implementation.

**What we know (from live HTML analysis):**
- The index page (`index.php?fecha=DD-MM-YYYY`) renders the full daily sumario in HTML without Turnstile blocking.
- Each edicto has a predictable filename pattern: `{YYYYMMDD}-{edictoid}-{year}-{supplement}.pdf`
- PDF download goes through a PHP handler: `/verificacion.php?archivo={filename}.pdf`
- The handler is protected by **Cloudflare Turnstile** (client-side JS token: `0x4AAAAAACh9RT4ulRL2covD`)
- The `descargarConVerificacion()` JS function attaches the Turnstile token to the download request.

**Investigation required (spike task):**
Attempt a direct `GET /verificacion.php?archivo={filename}.pdf` with browser-realistic headers and a valid session cookie. If Turnstile is purely client-side validation (not server-side), the PDF may be reachable directly.

```
# Spike test:
GET https://www.bopmalaga.es/verificacion.php?archivo=20260715-02598-2026-00.pdf
Headers:
  User-Agent: Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)...
  Referer: https://www.bopmalaga.es/index.php
  Cookie: {session cookie from prior page load}
```

**Fallback options if direct download is blocked:**
1. **Headless Browser (Playwright for Go / `go-rod`)** — resolves Turnstile automatically but adds ~300MB Chromium dependency and increases complexity significantly.
2. **Contact Diputación de Málaga** — request official feed or bulk data access; BOPMA is legally public information.
3. **Implement partial support** — scrape the HTML sumario for metadata (without PDFs) and trigger manual download via CVE verification URL.

**Complexity:** ⚠️ High / Unknown — blocked pending spike investigation.

---

## Requirements

### Requirement: BOE Daily Scraping via Open Data API
The scraper SHALL fetch the BOE daily sumario and download all PDFs using the official REST API.

#### Scenario: Successful BOE daily run
- **WHEN** the scraper scheduler triggers for BOE at the configured daily time
- **THEN** it SHALL call `GET /datosabiertos/api/boe/sumario/{YYYYMMDD}` and parse the JSON item list
- **AND** for each item, download the PDF from its `urlPdf` field
- **AND** upload each PDF to R2 at path `raw-documents/boe/{year}/{month}/{day}/{id}.pdf`
- **AND** publish one RabbitMQ ingestion event per document with source `BOE`

#### Scenario: BOE API unavailable
- **WHEN** the BOE API returns a non-200 status code or times out
- **THEN** the scraper SHALL log the error and skip that run without crashing
- **AND** SHALL retry the next day at the scheduled time

### Requirement: BOJA Daily Scraping via Atom Feed
The scraper SHALL fetch all configured BOJA section feeds and download individual disposition PDFs.

#### Scenario: Successful BOJA daily run
- **WHEN** the scraper scheduler triggers for BOJA
- **THEN** it SHALL fetch all configured section Atom feeds (`s51.xml` through `s55.xml`)
- **AND** parse each `<entry>` to extract the PDF link and metadata (organism, section, title)
- **AND** download each PDF directly from the official URL
- **AND** upload to R2 at path `raw-documents/boja/{year}/{boja_num}/{filename}.pdf`
- **AND** publish one RabbitMQ ingestion event per document with source `BOJA`

#### Scenario: Deduplication on re-run
- **WHEN** the scraper runs and a document with the same `documentId` was already uploaded to R2
- **THEN** it SHALL skip the upload and NOT publish a duplicate RabbitMQ event

### Requirement: BOPMA Scraping (Spike Required)
The scraper SHALL provide a BOPMA implementation after investigation of the Cloudflare Turnstile bypass feasibility.

#### Scenario: Spike — direct PDF download attempt
- **WHEN** a developer runs the BOPMA spike test
- **THEN** they SHALL attempt `GET /verificacion.php?archivo={filename}` with browser-realistic headers
- **AND** document whether the Turnstile check is enforced server-side or client-side only
- **AND** report the result to determine the implementation strategy

#### Scenario: BOPMA sumario HTML parsing (always feasible)
- **WHEN** the scraper fetches `https://www.bopmalaga.es/index.php?fecha={DD-MM-YYYY}`
- **THEN** it SHALL parse the HTML to extract all edicto IDs, titles, and organism names from the `<article>` elements
- **AND** build the filename pattern `{YYYYMMDD}-{edictoid}-{year}-{supplement}.pdf`
- **AND** attempt the PDF download via `/verificacion.php?archivo={filename}`

#### Scenario: BOPMA blocked by Turnstile
- **WHEN** the direct PDF download returns a Turnstile challenge (non-PDF content-type or 403)
- **THEN** the scraper SHALL log a warning with the blocked document ID and continue
- **AND** SHALL NOT crash or block the BOE/BOJA pipelines

### Requirement: Cloudflare R2 Storage Upload
The scraper SHALL upload all downloaded PDFs to Cloudflare R2 using the S3-compatible API.

#### Scenario: Uploading a PDF to R2
- **WHEN** a PDF is successfully downloaded
- **THEN** the scraper SHALL upload it to R2 with `Content-Type: application/pdf`
- **AND** set the object key to the source-specific path pattern
- **AND** confirm upload success before publishing the RabbitMQ event

#### Scenario: R2 upload failure
- **WHEN** an R2 upload fails (network error, credentials issue)
- **THEN** the scraper SHALL NOT publish a RabbitMQ event for that document
- **AND** SHALL log the failure with the document ID for manual retry

### Requirement: RabbitMQ Ingestion Event Publishing
After each successful upload, the scraper SHALL publish a structured JSON event to RabbitMQ.

#### Scenario: Publishing a valid ingestion event
- **WHEN** a PDF is successfully uploaded to R2
- **THEN** the scraper SHALL publish a JSON message to the `gazette-ingest-queue` with the following fields:
  - `eventId` (UUID v4)
  - `source` (enum: `BOE`, `BOJA`, `BOPMA`)
  - `documentId` (unique identifier from the source)
  - `title` (document title from metadata)
  - `originalUrl` (source URL of the document)
  - `storagePath` (R2 object key)
  - `publishedAt` (official publication date, ISO 8601)
  - `scrapedAt` (timestamp of scraping, ISO 8601)
  - `metadata` (organism, section, department, file size)

### Requirement: Scheduler and Echo Health Endpoint
The scraper SHALL run on a daily schedule and expose an HTTP health endpoint for Aspire monitoring.

#### Scenario: Daily scheduled execution
- **WHEN** the configured cron expression triggers (e.g. `0 7 * * 1-5` — weekdays at 7:00 AM)
- **THEN** the scraper SHALL run BOE and BOJA scrapers sequentially
- **AND** run the BOPMA scraper if its implementation is available

#### Scenario: Health check endpoint
- **WHEN** the .NET Aspire AppHost or a load balancer calls `GET /health`
- **THEN** the Echo server SHALL return HTTP 200 with `{ "status": "healthy" }`

#### Scenario: Manual trigger endpoint (development only)
- **WHEN** a developer calls `POST /scrape?source=BOE` during development
- **THEN** the scraper SHALL run the requested source's pipeline immediately
- **AND** this endpoint SHALL be disabled or protected in production
