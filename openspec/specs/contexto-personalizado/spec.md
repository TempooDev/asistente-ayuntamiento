# Specification: Contexto Personalizado (User Interests)

## Purpose
Store each user's topics of interest and locations, let them edit them in their profile, and use them to tailor the assistant's answers.

## Requirements

### Requirement: Stored Preferences per User and Tenant
The system SHALL store a user's preferences (`Topics` and `Locations`) in PostgreSQL, keyed by the Auth0 user id and the tenant. A user SHALL only read and change their own preferences in the current tenant.

#### Scenario: Reading the preferences
- **WHEN** an authenticated user calls `GET /api/users/me/preferences`
- **THEN** the API SHALL return their topics and locations in the current tenant, or empty lists if none were saved

#### Scenario: Same user in another organization
- **WHEN** the same Auth0 user signs in to a different organization
- **THEN** the preferences of the first organization SHALL NOT be returned

### Requirement: Editing in the Profile Page
The Angular profile page SHALL show the current topics and locations and let the user add, edit and remove them; saving SHALL replace the stored lists (`PUT /api/users/me/preferences`).

#### Scenario: Removing an interest
- **WHEN** the user removes a topic and saves
- **THEN** the next `GET` SHALL no longer return it

### Requirement: Preferences in the Answers
When the baseline pipeline answers with retrieved context, the system prompt SHALL include the user's topics and locations, so the answer can prioritize them.

#### Scenario: User interested in housing in Málaga
- **WHEN** a user with topic "Vivienda" and location "Málaga" asks a question with matching documents
- **THEN** the system prompt sent to the model SHALL mention both
