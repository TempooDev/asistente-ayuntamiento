## 1. Database and Entities

- [x] 1.1 Create `ScraperFilterRule` entity and add it to `ApplicationDbContext`
- [x] 1.2 Create and apply EF Core migration for the new table
- [x] 1.3 Implement repository or direct DbContext methods for CRUD operations

## 2. API Endpoints (.NET)

- [x] 2.1 Add MediatR commands/queries for creating, reading, updating, and deleting `ScraperFilterRule`
- [x] 2.2 Create protected HTTP endpoints (controllers/minimal APIs) to expose the CRUD operations to the Angular UI

## 3. gRPC Contracts

- [x] 3.1 Create `scraper.proto` defining `FilterConfigService` and `ScraperCommandService`
- [x] 3.2 Add gRPC tooling/build configuration to the .NET API project to generate C# classes
- [x] 3.3 Add gRPC tooling to the Go scraper project to generate Go code from the `.proto` file

## 4. gRPC Server (.NET)

- [x] 4.1 Implement `FilterConfigService` in the .NET API to return active rules from the database
- [x] 4.2 Configure and register the gRPC endpoint in `Program.cs` of the API

## 5. gRPC Client (Go Scraper)

- [x] 5.1 Implement a gRPC client in Go to call `FilterConfigService`
- [x] 5.2 Integrate the fetched rules into the scraping loop to filter out non-matching documents in-memory

## 6. On-Demand Scrape Trigger

- [x] 6.1 Implement `ScraperCommandService` gRPC server in the Go scraper to trigger synchronous extraction
- [x] 6.2 Implement a gRPC client in the .NET API to call `ForceScrape` on the Go server
- [x] 6.3 Expose an HTTP endpoint in the .NET API for the Angular UI to trigger the manual scrape

## 7. Aspire Orchestration

- [x] 7.1 Update `AppHost` to wire up the gRPC endpoints properly between the .NET ApiService and the Go Scraper
