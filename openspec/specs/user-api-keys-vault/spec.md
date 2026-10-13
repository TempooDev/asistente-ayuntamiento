# Specification: Tenant AI Model Configuration

> Kept under the folder name `user-api-keys-vault` for history. The original design (per-user keys in an Infisical vault, a model selector in the chat) was not built; what exists is a per-tenant configuration with keys encrypted by ASP.NET Core Data Protection. Moving the keys to an external vault is a possible follow-up change, not a requirement of this spec.

## Purpose
Let the administrators of each tenant (Auth0 organization) choose the chat model their users talk to and store its API key securely, while the platform keeps control of the embedding model and of a fallback model.

## Requirements

### Requirement: Per-tenant Chat Model Settings
Each tenant SHALL have at most one `AiConfiguration` row (provider, model, temperature, optional endpoint URL, encrypted API key). Supported providers: `ollama`, `openai`, `openrouter` (OpenAI-compatible endpoint) and `google` (Gemini). Without a row, the platform defaults (`Ai:Chat:*`) apply.

#### Scenario: Tenant without configuration
- **WHEN** a user of a tenant without `AiConfiguration` chats
- **THEN** the answer SHALL come from the platform default model and key (`Ai:Chat:Provider`, `Ai:Chat:Model`, `Ai:Chat:ApiKey`)

#### Scenario: Provider that needs a key without one
- **WHEN** the tenant selects `openai`, `openrouter` or `google` and neither the tenant nor the platform has a key
- **THEN** chat requests SHALL fail with the typed code `LLM_NOT_CONFIGURED` and a user-facing message, without provider details

### Requirement: Encrypted, Write-only API Keys
API keys SHALL be stored encrypted with ASP.NET Core Data Protection (purpose `AiConfiguration.ApiKey`) and SHALL never be returned by the API.

#### Scenario: Reading the configuration
- **WHEN** an administrator opens the AI settings (`GET /api/settings/ai`)
- **THEN** the response SHALL include `hasApiKey` but not the key
- **AND** the form SHALL show the key as stored and only send a new one if the administrator types it

#### Scenario: Saving without a new key
- **WHEN** an administrator saves the settings with an empty API key
- **THEN** the stored key SHALL be kept

#### Scenario: Key that cannot be decrypted
- **WHEN** the stored key cannot be decrypted (for example after the key ring changed)
- **THEN** the system SHALL log a warning and fall back to the platform key instead of failing every request

### Requirement: Administrators Only
Reading and changing the AI settings SHALL require the `administrador` role (`Policies.Admin`) and SHALL only affect the caller's tenant.

#### Scenario: Regular user
- **WHEN** a user without the `administrador` role calls `GET` or `PUT /api/settings/ai`
- **THEN** the API SHALL answer 403

#### Scenario: Unauthenticated caller
- **WHEN** the call has no valid token
- **THEN** the API SHALL answer 401

### Requirement: Platform-owned Embeddings and Fallback
The embedding model (`Ai:Embeddings:*`) SHALL be platform-wide and not configurable per tenant, because every tenant searches the same vector collections. When the tenant's model reports an exhausted quota (HTTP 429), the platform fallback model (`Ai:Fallback:*`) MAY answer with its own credentials.

#### Scenario: Quota exhausted with a fallback configured
- **WHEN** the tenant's provider answers 429 and `Ai:Fallback:ApiKey` is set
- **THEN** the answer SHALL come from the fallback model with a notice for the user
- **AND** the tenant's key SHALL never be sent to the fallback provider

#### Scenario: Quota exhausted without fallback
- **WHEN** the tenant's provider answers 429 and no fallback is configured
- **THEN** the request SHALL fail with `LLM_QUOTA_EXCEEDED`
