# Specification: Blazor Chat with Conversation History

## Purpose
Provide a responsive chat interface that saves, lists, and reloads conversation sessions and their respective message history in PostgreSQL.

## Requirements

### Requirement: Session Management
The system SHALL support creating, listing, and renaming conversation sessions.

#### Scenario: Initializing a new chat session
- **WHEN** the user requests a new chat session
- **THEN** the API SHALL create a new `ChatSession` record in the database and return it with a unique UUID

#### Scenario: Renaming a session automatically
- **WHEN** the user sends the first message in a session
- **THEN** the system SHALL update the `ChatSession` title to the first 30 characters of that user prompt

### Requirement: Message History Storage
The system SHALL persist all chat messages with their sender role and sources.

#### Scenario: Storing message turns
- **WHEN** a chat exchange occurs
- **THEN** the system SHALL record two separate `ChatMessage` entries associated with the active `ChatSession` containing the content, role, and cited source documents in JSON format

#### Scenario: Loading history
- **WHEN** the user loads an existing session ID
- **THEN** the API SHALL fetch and return the list of all `ChatMessage` records associated with that session sorted chronologically
