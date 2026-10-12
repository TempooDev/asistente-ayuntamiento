## ADDED Requirements

### Requirement: CI Covers All Codebases
The CI pipeline SHALL build and test the .NET solution, the Angular SPA and the Go scraper on every push to `main` and on every pull request, and SHALL verify that the generated protobuf code is up to date.

#### Scenario: Pull request with a failing Angular spec
- **WHEN** a pull request introduces an Angular spec that fails or does not compile
- **THEN** the CI `angular` job SHALL fail and block the merge

#### Scenario: Pull request with a Go vet or test failure
- **WHEN** `go vet ./...` or `go test ./...` fails
- **THEN** the CI `go` job SHALL fail

#### Scenario: Stale generated protos
- **WHEN** `src/Protos/scraper.proto` changes without regenerating the Go code
- **THEN** the `protos` job SHALL fail on `git diff --exit-code`

### Requirement: Deployment Gated on CI with Immutable Tags
Deployment SHALL only run after a successful CI run for the same commit, and SHALL publish container images tagged with the commit SHA as well as `latest`. Image names SHALL match the names referenced in `docker-compose.yml`.

#### Scenario: CI fails
- **WHEN** CI fails for commit `abc123`
- **THEN** the deploy workflow SHALL NOT publish or deploy images for `abc123`

#### Scenario: Rollback
- **WHEN** an operator needs to roll back
- **THEN** they SHALL be able to deploy the image tag `sha-<previous>` without rebuilding

### Requirement: Central Package Management and Warning Policy
NuGet versions SHALL be defined once in `Directory.Packages.props`, with no conflicting versions of the same package across projects. The build SHALL treat nullable warnings and known-vulnerability warnings (`NU1901`–`NU1904`) as errors.

#### Scenario: Vulnerable package introduced
- **WHEN** a package with a known high-severity vulnerability is restored
- **THEN** the build SHALL fail

#### Scenario: Version drift
- **WHEN** a project declares a `Version` attribute on a `PackageReference`
- **THEN** the build SHALL fail, because central package management is enforced

### Requirement: Repository Hygiene
The repository SHALL NOT track compiled binaries, ad-hoc patch files, one-off scripts, duplicated generated code or obsolete tooling. Agent skill definitions SHALL live in a single canonical folder per tool, and `.gitignore` rules SHALL be valid (no inline comments).

#### Scenario: Building the Go scraper locally
- **WHEN** a developer runs `go build` in `src/go-scraper`
- **THEN** the produced binary SHALL be ignored by git

#### Scenario: Repository content check
- **WHEN** CI runs the hygiene check
- **THEN** it SHALL fail if any tracked file is larger than 5 MB outside an allow-list (fonts and logos in `memoria/`)

### Requirement: Minimum Test Baseline
Each codebase SHALL have meaningful tests for its critical paths. Placeholder tests and empty test projects SHALL NOT exist.
- **.NET:**
  - integration tests (WebApplicationFactory + Testcontainers) for the authorization matrix, tenant isolation and the ack/nack/DLQ cycle;
  - unit tests for `HybridRetrievalService`, the arena analytics, chat streaming fallback and ingestion idempotency.
- **Angular:** specs for the guards, the hub factory, `ChatStore` and `MarkdownService`.
- **Go:** fixture-based tests for the BOE/BOJA parsers, the filter matching, and the HTTP client retry behaviour.

#### Scenario: Placeholder test detection
- **WHEN** a test project contains only template tests (e.g. `UnitTest1.cs` with an empty `Test1`)
- **THEN** the hygiene check SHALL fail

#### Scenario: Tenant isolation regression
- **WHEN** a change makes an endpoint return data of tenant `org_B` to a user of `org_A`
- **THEN** the tenant-isolation integration tests SHALL fail
