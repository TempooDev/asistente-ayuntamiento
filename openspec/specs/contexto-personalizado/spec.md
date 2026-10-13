# Specification: Contexto Personalizado (User Interests)

## Purpose
Store each user's topics of interest and locations per organization, let them maintain the lists in their profile, and use them to tailor the baseline pipeline's answers.

## Requirements

### Requirement: Stored Preferences per User and Tenant
The system SHALL store a user's preferences (`Topics` and `Locations`) in `UserPreference`, unique on (`Auth0UserId`, `TenantId`) and under the tenant query filter. A user SHALL only read and change their own preferences in the current tenant. Saved values SHALL be trimmed, empty values dropped and duplicates removed case-insensitively.

#### Scenario: Reading the preferences
- **WHEN** an authenticated user calls `GET /api/users/me/preferences`
- **THEN** the API SHALL return their topics and locations in the current tenant, or empty lists if none were saved

#### Scenario: Same user in another organization
- **WHEN** the same Auth0 user signs in to a different organization
- **THEN** the preferences of the first organization SHALL NOT be returned or changed

#### Scenario: Unauthenticated call
- **WHEN** the call has no valid token
- **THEN** the API SHALL answer 401

### Requirement: Maintaining Interests in the Profile Page
The Angular profile page SHALL show the current topics and locations. In edit mode the user SHALL be able to add new entries and remove existing ones (there is no in-place editing of an entry). "Guardar Cambios" SHALL save the profile and then replace the stored lists with `PUT /api/users/me/preferences`; cancelling SHALL reload the stored values.

#### Scenario: Removing an interest
- **WHEN** the user removes a topic in edit mode and saves
- **THEN** the next `GET` SHALL no longer return it

#### Scenario: Adding a duplicate
- **WHEN** the user adds a topic that is already in the list
- **THEN** the page SHALL not add it again

### Requirement: Preferences in the Answers
The baseline pipeline (`BaselineRagPipeline`) SHALL append the user's topics and locations to its system prompt (`PromptCatalog.UserContext`) when the history has no system message yet: always on the non-streaming path (`PromptCatalog.ChatSystem`), and on the streaming path only when documents were retrieved (`PromptCatalog.StreamingSystem`; without documents it uses `PromptCatalog.ConversationalSystem` without preferences). A user without preferences SHALL get no user-context block. The hierarchical pipeline does not use preferences.

#### Scenario: User interested in housing in Málaga
- **WHEN** a user with topic "Vivienda" and location "Málaga" streams a question that retrieves documents
- **THEN** the system prompt sent to the model SHALL mention both

#### Scenario: Small talk
- **WHEN** the same user streams a message that retrieves no documents
- **THEN** the system prompt SHALL be the conversational one, without the user-context block
