# Specification: User API Keys with Infisical Vault & Dynamic Kernel

## Purpose
Allow users to store their own LLM provider API keys securely in an Infisical vault (internal-only network), select their active model from the chat UI, and have the system build a per-request Semantic Kernel instance using the user's key. The embedding model remains fixed as local Ollama.

## Requirements

### Requirement: IApiKeyVault Abstraction
The system SHALL define an `IApiKeyVault` interface with implementations for Infisical (production) and in-memory (development).

#### Scenario: Storing a user API key
- **WHEN** a user submits an API key for a provider via the settings page
- **THEN** the system SHALL call `IApiKeyVault.StoreAsync(userId, provider, apiKey)`
- **AND** the key SHALL be stored at path `/users/{userId}/{provider}` in Infisical
- **AND** the raw key SHALL never be persisted in the PostgreSQL database

#### Scenario: Retrieving a key in development
- **WHEN** the application runs in Development environment
- **THEN** the `InMemoryApiKeyVault` implementation SHALL be used
- **AND** no Infisical container SHALL be required to run the app locally

### Requirement: Infisical Network Isolation
Infisical SHALL only be reachable from the ApiService on the internal Docker network.

#### Scenario: Infisical not exposed publicly
- **WHEN** the Docker Compose stack is running in production
- **THEN** the Infisical container SHALL have no externally bound ports
- **AND** only the ApiService container SHALL be able to reach it via internal DNS (e.g. `http://infisical:8080`)

### Requirement: Dynamic KernelFactory per Request
The system SHALL build a new `Kernel` instance per chat request using the user's active provider and API key.

#### Scenario: User has an OpenAI key configured
- **WHEN** a chat request arrives and the user's active provider is `OpenAI`
- **THEN** `KernelFactory` SHALL retrieve the key from Infisical and build a Kernel with `AddOpenAIChatCompletion(modelId, apiKey)`

#### Scenario: User has a DeepSeek key configured
- **WHEN** a chat request arrives and the user's active provider is `DeepSeek`
- **THEN** `KernelFactory` SHALL build a Kernel with `AddOpenAIChatCompletion(modelId, apiKey, endpoint: "https://api.deepseek.com")`

#### Scenario: User has a Google Gemini key configured
- **WHEN** a chat request arrives and the user's active provider is `Google`
- **THEN** `KernelFactory` SHALL build a Kernel with the Gemini connector using the user's key

#### Scenario: User has an Anthropic Claude key configured
- **WHEN** a chat request arrives and the user's active provider is `Claude`
- **THEN** `KernelFactory` SHALL build a Kernel with the Anthropic connector using the user's key

#### Scenario: No API key configured by the user
- **WHEN** a chat request arrives and the user has no active provider configured
- **THEN** the system SHALL fall back to the platform Ollama instance (llama3.2)
- **AND** SHALL NOT return an error, allowing the user to chat without their own key

### Requirement: Model Selector in Chat UI
The Blazor UI SHALL display a model selector showing only providers the user has configured, plus the Ollama fallback.

#### Scenario: Displaying available models
- **WHEN** an authenticated user opens the chat
- **THEN** the UI SHALL query `/api/apikeys/providers` and render a dropdown
- **AND** the dropdown SHALL include only providers with a stored key plus "Ollama (local)"

#### Scenario: User changes active model
- **WHEN** the user selects a different model from the dropdown
- **THEN** the selection SHALL be persisted as the user's `activeProvider` preference
- **AND** all subsequent chat requests in that session SHALL use the newly selected provider

### Requirement: API Key Management Page
Users SHALL be able to add, view (masked), and delete their API keys from a settings page.

#### Scenario: Adding a new API key
- **WHEN** the user submits a key for a provider they have not configured yet
- **THEN** the system SHALL store it in Infisical and show the provider as active in the model selector

#### Scenario: Deleting an API key
- **WHEN** the user deletes a provider key
- **THEN** the system SHALL call `IApiKeyVault.DeleteAsync(userId, provider)`
- **AND** the provider SHALL be removed from the model selector dropdown
- **AND** if it was the active provider, the system SHALL automatically fall back to Ollama

#### Scenario: Viewing existing keys
- **WHEN** the user navigates to the settings page
- **THEN** configured providers SHALL be shown with masked keys (e.g. `sk-••••••••4f2a`)
- **AND** the raw key SHALL never be returned from the API to the client
