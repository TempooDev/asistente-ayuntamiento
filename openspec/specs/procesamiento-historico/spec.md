# Specification: Procesamiento Histórico (Interests from the Chat History)

## Purpose
Suggest topics and locations for a user's profile by analyzing their recent conversations with the platform chat model, in the background, without blocking the request.

## Requirements

### Requirement: On-demand Background Analysis
`POST /api/users/me/preferences/analyze` SHALL queue the analysis on the in-process background work queue (`IBackgroundWorkQueue`, capacity 200) as a work item bound to the caller's tenant, and SHALL return 202 at once. When the queue is full, it SHALL answer 503 and analyze nothing. The profile page SHALL start it with the "Analizar mi historial" button.

#### Scenario: Analysis requested
- **WHEN** an authenticated user with a tenant requests the analysis
- **THEN** the API SHALL answer 202 and the analysis SHALL run after the request, scoped to the same tenant
- **AND** the profile page SHALL tell the user to reload the page in a few seconds

#### Scenario: Queue full
- **WHEN** the background work queue cannot accept more items
- **THEN** the API SHALL answer 503 with a user-facing message

#### Scenario: Caller without user id or tenant
- **WHEN** the token has no `sub` or no organization
- **THEN** the API SHALL answer 401 and queue nothing

### Requirement: Extraction from Recent Sessions
The analysis (`HistoryAnalyzerService`) SHALL read the user's 5 most recent chat sessions in the tenant, cut the text to at most 8000 characters, ask the platform chat model (`Ai:Chat:*`, prompt `PromptCatalog.HistoryAnalyzerSystem`) for topics and locations as JSON, and merge the result into the stored preferences without duplicates (case-insensitive). Its LLM call SHALL be measured like the chat ones (`app.pipeline=profile`, `app.llm.operation=history_analysis`).

#### Scenario: New interests found
- **WHEN** the model returns topics or locations the user does not have yet
- **THEN** they SHALL be added to the existing ones, and existing ones SHALL be kept

#### Scenario: Nothing new
- **WHEN** every returned value is already stored
- **THEN** the preferences SHALL NOT be written

#### Scenario: Invalid model answer
- **WHEN** the model does not return valid JSON (Markdown code fences are removed first)
- **THEN** the preferences SHALL stay unchanged

#### Scenario: User without conversations
- **WHEN** the user has no chat sessions in the tenant
- **THEN** no model call SHALL be made
