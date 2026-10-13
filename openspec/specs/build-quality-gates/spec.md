# build-quality-gates Specification

## Purpose
Keep the repository healthy through CI: required checks for repository hygiene, .NET, Angular, Go and generated protos; central package versions with nullable and vulnerability warnings as errors; automatic deploys only after a green CI run, with images tagged by commit.

## Requirements
### Requirement: CI Covers All Codebases
The CI workflow (`.github/workflows/ci.yml`) SHALL run on pushes to `main`, `feature/**`, `feat/**` and `fix/**`, on every pull request and on manual dispatch, with these jobs:
- `hygiene`: rejects tracked files over 5 MB;
- `dotnet`: restore, `dotnet format whitespace --verify-no-changes`, Release build and `dotnet test` of `AsistenteAyuntamiento.slnx`;
- `angular`: `ng test --watch=false` and a production build;
- `go`: `go vet ./...`, `go test -race ./...`, `gofmt -l` and a build of `./cmd/scraper`;
- `protos`: regenerates the Go code from `src/Protos` with pinned `protoc` and plugin versions and fails on `git diff --exit-code`.

#### Scenario: Pull request with a failing Angular spec
- **WHEN** a pull request introduces an Angular spec that fails or does not compile
- **THEN** the `angular` job SHALL fail

#### Scenario: Pull request with a Go vet, test or format failure
- **WHEN** `go vet`, `go test -race` fails or `gofmt -l cmd internal` lists a file
- **THEN** the `go` job SHALL fail

#### Scenario: Stale generated protos
- **WHEN** `src/Protos/scraper.proto` changes without regenerating `src/go-scraper/internal/protos`
- **THEN** the `protos` job SHALL fail

### Requirement: Automatic Deployment Gated on CI with Commit Tags
The deploy workflow (`.github/workflows/deploy.yml`) SHALL run automatically only through `workflow_run` after the CI workflow completes successfully for a push to `main`, and SHALL build the commit CI tested (`workflow_run.head_sha`). It SHALL build only the images whose sources changed in that commit, push them tagged `sha-<7-char short sha>` and `latest` to `ghcr.io/tempoodev/asistente-ayuntamiento-*` (the names referenced in `docker-compose.yml`), and then call the Dokploy webhook. A manual `workflow_dispatch` SHALL only run from `main` and only for a commit with a successful CI run on push; it then builds the selected services. `docker-compose.yml` SHALL reference the `:latest` tag of every application image.

#### Scenario: CI fails
- **WHEN** CI fails for a push of commit `abc1234` to `main`
- **THEN** the deploy workflow SHALL NOT build, push or deploy images for `abc1234`

#### Scenario: CI succeeds on main
- **WHEN** CI succeeds for a push to `main` that changes `src/AsistenteAyuntamiento.ApiService/`
- **THEN** the ApiService image SHALL be pushed as `sha-<short sha>` and `latest`
- **AND** the Dokploy deployment SHALL be triggered

#### Scenario: Manual run
- **WHEN** an operator runs the workflow manually with `force_api`
- **THEN** the ApiService image SHALL be built and deployed only if the run is on `main` and CI succeeded on push for that commit

#### Scenario: Manual run without a green CI
- **WHEN** an operator runs the workflow manually from another branch, or for a `main` commit whose CI failed or is still running
- **THEN** the workflow SHALL fail before building anything

### Requirement: Central Package Management and Warning Policy
NuGet versions SHALL be defined once in `Directory.Packages.props` (`ManagePackageVersionsCentrally`, with transitive pinning). `Directory.Build.props` SHALL enable nullable reference types and `AnalysisLevel=latest-recommended`, and SHALL treat nullable warnings and known-vulnerability warnings (`NU1901`–`NU1904`) as errors. Other analyzer and `.editorconfig` diagnostics (naming, `CA2254`, `CA2016`) are warnings and SHALL NOT fail the build.

#### Scenario: Vulnerable package introduced
- **WHEN** a package with a known vulnerability is restored
- **THEN** the build SHALL fail

#### Scenario: Version on a package reference
- **WHEN** a project declares a `Version` attribute on a `PackageReference`
- **THEN** restore SHALL fail, because central package management is enabled

### Requirement: Repository Hygiene
The repository SHALL NOT track files larger than 5 MB; the CI `hygiene` job SHALL enforce it with an allow-list regex that is empty. Go build output (`/go-scraper`, `/bin/`, `*.exe`, `tmp_scraper_bin` under `src/go-scraper`) and .NET build output SHALL be ignored by git.

#### Scenario: Large file committed
- **WHEN** a commit tracks a file larger than 5 MB
- **THEN** the `hygiene` job SHALL fail and list the file with its size

#### Scenario: Building the Go scraper into bin
- **WHEN** a developer runs `go build -o bin/scraper ./cmd/scraper` in `src/go-scraper`
- **THEN** the produced binary SHALL be ignored by git

### Requirement: Test Baseline
Each codebase SHALL keep tests for its critical paths, run by CI:
- **.NET integration (ApiService.Tests):** `WebApplicationFactory` with header-based test authentication and an EF Core InMemory database for the authorization matrix, tenant isolation, hub isolation and hub stream errors and the internal gRPC key; plus unit tests of the startup configuration checks.
- **.NET integration (Worker.Tests):** `WebApplicationFactory` over the Worker in Production (no consumers, only `live` checks) for `/health`, `/alive` and the `--health-probe` mode; plus unit tests of the Worker startup configuration checks.
- **.NET integration (Infrastructure.Tests):** Testcontainers (RabbitMQ, PostgreSQL, Qdrant) for the consumer ack/nack/DLQ cycle, ingestion idempotency and job-state concurrency; these tests are skipped when Docker is unavailable.
- **.NET unit:** Application.Tests (e.g. `HybridRetrievalService`, arena analytics, chat streaming fallback, LLM metrics), Domain.Tests (document job state), Gateway.Tests (Faro forwarding) and ArchitectureTests.
- **Angular:** specs for the guards, the hub connection factory, `ChatStore`, `MarkdownService` and the stream-error mapping, among others.
- **Go:** fixture-based tests (`testdata`) for the BOE and BOJA clients, filter matching, and the HTTP client retry behaviour.

#### Scenario: Tenant isolation regression
- **WHEN** a change makes an endpoint return data of tenant `org_B` to a user of `org_A`
- **THEN** the tenant-isolation integration tests SHALL fail

#### Scenario: Consumer stops acknowledging messages
- **WHEN** a change breaks the ack/nack/DLQ handling of the RabbitMQ consumer
- **THEN** `RabbitMqConsumerIntegrationTests` SHALL fail on a runner with Docker
