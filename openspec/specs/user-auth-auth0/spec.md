# Specification: Auth0 User Authentication & Data Isolation

## Purpose
Integrate Auth0 as the delegated identity provider so that every user authenticates via OIDC, the API validates JWTs, and all user-scoped data (chat sessions, messages, API keys) is strictly filtered by the Auth0 `sub` claim.

## Requirements

### Requirement: Auth0 OIDC Login in Blazor Auto
The system SHALL authenticate users via Auth0 using the OIDC PKCE flow compatible with Blazor Auto render mode.

#### Scenario: User logs in successfully
- **WHEN** an unauthenticated user accesses the application
- **THEN** the system SHALL redirect them to the Auth0 Universal Login page
- **AND** upon successful login, redirect back with a JWT containing the `sub`, `email`, and `name` claims
- **AND** the Blazor app SHALL store the session and provide the JWT to downstream API calls

#### Scenario: Unauthenticated access to protected page
- **WHEN** a user without a valid session attempts to access the chat or settings pages
- **THEN** the system SHALL redirect them to Auth0 login
- **AND** SHALL NOT expose any data or API endpoints

### Requirement: JWT Bearer Validation in ApiService
The ApiService SHALL validate Auth0 JWTs on every protected endpoint and SignalR hub.

#### Scenario: Valid JWT on API request
- **WHEN** the ApiService receives a request with a valid Auth0 Bearer token
- **THEN** it SHALL extract the `sub` claim as the `userId` and allow the request to proceed

#### Scenario: Invalid or missing JWT
- **WHEN** the ApiService receives a request with a missing, expired, or tampered token
- **THEN** it SHALL return HTTP 401 Unauthorized
- **AND** the SignalR hub SHALL reject the connection

### Requirement: Chat Session Data Isolation by UserId
All chat session queries SHALL be scoped to the authenticated user's `sub` claim.

#### Scenario: Loading chat session list
- **WHEN** an authenticated user requests their chat session list
- **THEN** the system SHALL query only `ChatSession` records where `UserId` matches the Auth0 `sub` claim
- **AND** SHALL NOT return sessions belonging to other users

#### Scenario: Accessing another user's session
- **WHEN** a user requests a `ChatSession` by ID that belongs to a different user
- **THEN** the system SHALL return HTTP 404 Not Found (not 403, to avoid enumeration)

### Requirement: AppHost Local Development Setup
The development environment SHALL support Auth0 without requiring a local container.

#### Scenario: Running locally with Aspire
- **WHEN** the developer starts the AppHost
- **THEN** Auth0 credentials (Domain, ClientId, Audience) SHALL be loaded from Aspire User Secrets
- **AND** the app SHALL connect to the Auth0 tenant configured for development
