## ADDED Requirements

### Requirement: Tenant Credentials Are Only Sent to Their Own Provider
A tenant's stored LLM API key SHALL only be sent to the provider and endpoint configured for that key. Fallback providers SHALL use platform credentials from configuration (`Ai:Fallback:*`).

#### Scenario: Quota exceeded with fallback configured
- **GIVEN** a tenant configured with provider `google` and its own API key
- **AND** `Ai:Fallback:ApiKey` is set
- **WHEN** the Google call fails with HTTP 429
- **THEN** the system SHALL retry against the fallback provider using `Ai:Fallback:ApiKey`
- **AND** the outgoing request to the fallback endpoint SHALL NOT contain the tenant's Google key

#### Scenario: Quota exceeded without fallback configured
- **GIVEN** `Ai:Fallback:ApiKey` is empty
- **WHEN** the provider call fails with HTTP 429
- **THEN** the system SHALL return the typed error `LLM_QUOTA_EXCEEDED` to the client
- **AND** SHALL NOT call any other provider

#### Scenario: Non-quota error containing the word "limit"
- **WHEN** the provider fails with a non-429 error whose message contains "limit" (e.g. "context length limit")
- **THEN** the system SHALL NOT trigger the fallback path
- **AND** SHALL report `LLM_UNAVAILABLE` or a more specific code

### Requirement: TLS Certificate Validation
Outbound HTTP clients to LLM providers, object storage and internal gRPC services SHALL validate TLS certificates. A certificate-validation bypass SHALL only be possible in the Development environment, behind an explicit configuration flag that is `false` by default.

#### Scenario: Production call with an invalid certificate
- **GIVEN** the environment is Production
- **WHEN** an LLM provider endpoint presents an invalid certificate
- **THEN** the call SHALL fail with a TLS error
- **AND** no API key SHALL be transmitted

#### Scenario: Insecure flag outside Development
- **WHEN** `Ai:Http:AllowInsecureTls=true` is set in a non-Development environment
- **THEN** the application SHALL refuse to start

### Requirement: Pooled HTTP Clients for LLM Providers
LLM chat clients SHALL be built through `IChatModelFactory` with `IHttpClientFactory` named clients. Code in the Application layer SHALL NOT instantiate `HttpClient` or `HttpClientHandler` directly.

#### Scenario: Many sequential chat requests
- **WHEN** 1,000 chat requests are processed sequentially
- **THEN** the number of TCP connections in TIME_WAIT caused by LLM calls SHALL stay bounded by the handler pool
- **AND** no `HttpClient` instance SHALL be created per request by Application code

### Requirement: Embedding Generator Resolution
Retrieval SHALL resolve the embedding generator from the platform embedding service (the local Ollama embedding model), never from the per-tenant chat kernel.

#### Scenario: Non-streaming completion with a user message
- **WHEN** `GetCompletionAsync` is called with a non-empty user message
- **THEN** the system SHALL generate the query embedding with the platform embedding generator
- **AND** SHALL return `Success = true` with retrieved sources when Qdrant returns matches
