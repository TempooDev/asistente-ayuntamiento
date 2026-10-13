# Specification: Procesamiento Histórico (Interests from the Chat History)

## Purpose
Suggest topics and locations for a user's profile by analyzing their recent conversations with the platform model, without blocking the request.

## Requirements

### Requirement: On-demand Background Analysis
`POST /api/users/me/preferences/analyze` SHALL queue the analysis on the background work queue with the caller's tenant and return 202 at once. When the queue is full, it SHALL answer 503 and analyze nothing.

#### Scenario: Analysis requested
- **WHEN** an authenticated user requests the analysis
- **THEN** the API SHALL answer 202 and the analysis SHALL run after the request, scoped to the same tenant

#### Scenario: Queue full
- **WHEN** the background work queue cannot accept more items
- **THEN** the API SHALL answer 503 with a user-facing message

### Requirement: Extraction from Recent Sessions
The analysis SHALL read the user's 5 most recent chat sessions in the tenant, at most 8000 characters, ask the platform chat model for topics and locations as JSON, and merge the result into the stored preferences without duplicates (case-insensitive). Its LLM call SHALL be measured like the chat ones (`app.pipeline=profile`, `app.llm.operation=history_analysis`).

#### Scenario: New interests found
- **WHEN** the model returns topics or locations the user does not have yet
- **THEN** they SHALL be added to the existing ones, and existing ones SHALL be kept

#### Scenario: Invalid model answer
- **WHEN** the model does not return valid JSON
- **THEN** the preferences SHALL stay unchanged

#### Scenario: User without conversations
- **WHEN** the user has no chat sessions
- **THEN** no model call SHALL be made
