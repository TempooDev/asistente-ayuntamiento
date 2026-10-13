# Specification: Auth0 User Authentication & Data Isolation

## Purpose
Use Auth0 as the identity provider: users sign in through Auth0 Universal Login with an organization, the API validates Auth0 access tokens on every protected endpoint and hub, and chat sessions are only reachable by the user (`sub`) and tenant that own them.

## Requirements
### Requirement: Auth0 Login in Angular
The SPA SHALL load its Auth0 settings (domain, client id, audience) at startup from `GET /api/config/auth0`, and SHALL authenticate users with `@auth0/auth0-angular` (authorization code flow with PKCE, refresh tokens, cache in `localstorage`). The SDK's HTTP interceptor SHALL attach the access token to requests for `/api/*` and `/hubs/*`. `authGuard` SHALL require an authenticated user with an organization context.

#### Scenario: Unauthenticated access to a protected page
- **WHEN** a user without a session navigates to a page protected by `authGuard` (e.g. `/chat`, `/perfil`)
- **THEN** the SPA SHALL redirect to Auth0 Universal Login with the last used organization and return to the requested URL after login

#### Scenario: Signed-in user without org_id who belongs to an organization
- **WHEN** the ID token has no `org_id` but lists organizations in the namespaced claim
- **THEN** the guard SHALL silently log in again (`prompt=none`) into the first organization

#### Scenario: User without any organization
- **WHEN** the signed-in user belongs to no organization
- **THEN** the SPA SHALL navigate to `/error?code=NO_ORGANIZATION`

#### Scenario: Auth0 settings unavailable
- **WHEN** `GET /api/config/auth0` fails or returns no domain or client id
- **THEN** protected routes SHALL navigate to `/error?code=AUTH_CONFIG_UNAVAILABLE` without starting a login

### Requirement: JWT Bearer Validation in ApiService
The ApiService SHALL validate Auth0 access tokens with authority `https://{Auth0:Domain}/` and audience `Auth0:Audience` on every endpoint or hub that requires authorization. For requests under `/hubs`, the token SHALL also be accepted from the `access_token` query string (WebSockets and Server-Sent Events). The `sub` claim SHALL identify the user.

#### Scenario: Valid token on an API request
- **WHEN** the ApiService receives a request to `/api/users/me` with a valid Auth0 bearer token
- **THEN** it SHALL use the `sub` claim as the user id and process the request

#### Scenario: Invalid or missing token
- **WHEN** a request to a protected endpoint has a missing, expired or tampered token
- **THEN** the API SHALL respond `401 Unauthorized`
- **AND** a connection to `/hubs/chat` or `/hubs/notifications` SHALL be rejected

### Requirement: Chat Session Isolation by User and Tenant
Chat sessions SHALL only be reached through the `ChatHub` (there are no HTTP chat-session endpoints), and every session query SHALL filter by `UserId == sub` and `TenantId == current tenant`. A session of another user or tenant SHALL be indistinguishable from a missing one.

#### Scenario: Loading the session list
- **WHEN** a user calls `GetSessions`
- **THEN** the hub SHALL return only that user's sessions in the current tenant

#### Scenario: Streaming into another user's session
- **WHEN** a user calls `StreamMessage` with the id of a session they do not own
- **THEN** the stream SHALL yield `Error [NOT_FOUND]: …` and end, without storing the message

#### Scenario: Loading or deleting another user's session
- **WHEN** a user calls `LoadSession` or `DeleteSession` with the id of a session they do not own
- **THEN** `LoadSession` SHALL return an empty list and `DeleteSession` SHALL do nothing

### Requirement: Model API Keys per Tenant
LLM provider API keys SHALL be stored per tenant in `AiConfiguration`, encrypted with ASP.NET Core Data Protection, managed by organization administrators through `/api/settings/ai`, and never returned to clients (the DTO only exposes `HasApiKey`).

#### Scenario: Organization administrator reads the AI settings
- **WHEN** an organization administrator calls `GET /api/settings/ai` for a tenant with a stored key
- **THEN** the response SHALL report `HasApiKey = true` without the key

### Requirement: AppHost Local Development Setup
The AppHost SHALL take the Auth0 settings from Aspire parameters (`auth0-domain`, `auth0-client-id`, `auth0-client-secret`, `auth0-audience`) stored in the AppHost user secrets and pass them to the ApiService, so local development uses a real Auth0 tenant without a local identity container.

#### Scenario: Running locally with Aspire
- **WHEN** the developer starts the AppHost with the Auth0 parameters set in user secrets
- **THEN** the ApiService SHALL receive `Auth0__Domain`, `Auth0__ClientId`, `Auth0__ClientSecret` and `Auth0__Audience`
- **AND** the SPA SHALL load them through `/api/config/auth0`
