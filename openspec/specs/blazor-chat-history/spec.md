# Specification: Chat Conversation History

> **Frontend note.** Kept under the folder name `blazor-chat-history` for history. The chat UI is now the Angular SPA (`features/chat`, state in `ChatStore`) talking to `/hubs/chat` (see `signalr-integration`); Blazor-specific details no longer apply.

## Purpose
Keep each user's chat conversations in PostgreSQL, scoped to the user and the tenant, so they can be listed, reopened and deleted, without making the answer wait for the database writes.

## Requirements

### Requirement: Session Management
The chat hub SHALL let a user create, list, open and delete their own `ChatSession`s (`CreateNewSession`, `GetSessions`, `LoadSession`, `DeleteSession`). Every lookup SHALL match the session id, the caller's `sub` and the caller's tenant. Sessions have no stored title: `GetSessions` SHALL return, newest first, the id, creation date, message count and a preview made of the first 80 characters of the first user message (followed by `...` when longer).

#### Scenario: New conversation
- **WHEN** the user starts a new conversation (or sends a first message with no conversation selected)
- **THEN** the hub SHALL create a `ChatSession` with a new UUID for that user and tenant and return its id

#### Scenario: Listing conversations
- **WHEN** the client calls `GetSessions`
- **THEN** it SHALL receive only the caller's sessions in the current tenant, newest first, each with its preview

#### Scenario: Deleting a conversation
- **WHEN** the user confirms the deletion of one of their sessions
- **THEN** the session and its messages SHALL be deleted
- **AND** deleting a session of someone else SHALL change nothing

### Requirement: Message Storage
The system SHALL persist each user and assistant message as a `ChatMessage` (session, role `user` or `assistant`, content, creation time). Sources are not stored separately; they are only part of the answer text. Messages SHALL be written asynchronously: the hub enqueues them in the singleton `ChatMessageBuffer` and `ChatPersistenceWorker` flushes the buffer to the database every 2 seconds.

#### Scenario: Normal turn
- **WHEN** the user sends a message and the streamed answer finishes
- **THEN** the user message and the full assistant answer SHALL be enqueued and saved by the next flush

#### Scenario: Answer interrupted by an error
- **WHEN** a chat stream fails (a model failure with an `LLM_*` code, or an internal error) after part of the answer was sent
- **THEN** the partial answer followed by the notice the client appends for that error (`\n\n**⚠️ <message>**`, the same text as `mapStreamError`; `HubErrors.StreamFailureNotice`) SHALL be enqueued as the assistant message
- **AND** reloading the conversation SHALL show the same text the user saw
- **AND** when no text was sent before the failure, no assistant message SHALL be stored

#### Scenario: Stream cancelled by the client
- **WHEN** the client cancels the stream (it disconnects or deletes the conversation) before the answer finishes
- **THEN** no assistant message SHALL be stored for that turn

#### Scenario: Database temporarily unavailable
- **WHEN** a flush fails with a transient database error
- **THEN** the messages SHALL stay buffered (up to 10,000) and be retried on the next flush

#### Scenario: Message that cannot be saved
- **WHEN** a message keeps failing for a non-transient reason (for example, its session was deleted meanwhile) for 3 attempts
- **THEN** it SHALL be copied to the dead-letter table `chat.FailedChatMessages`
- **AND** the other messages of the batch SHALL still be saved

#### Scenario: Shutdown
- **WHEN** the API stops
- **THEN** the worker SHALL flush the buffered messages once more before exiting

### Requirement: Arena Answers in the History
In Arena mode the assistant answer SHALL NOT be saved when the stream ends. It SHALL be saved only after the vote: the winning side's answer becomes the assistant message of the conversation; a tie or "both bad" adds no assistant message.

#### Scenario: User picks Beta
- **WHEN** the user votes `Beta` on an Arena answer
- **THEN** Beta's answer SHALL be enqueued as the assistant message of that conversation

#### Scenario: Tie
- **WHEN** the user votes `Tie`
- **THEN** no assistant message SHALL be stored for that turn

### Requirement: Loading History
`LoadSession(sessionId)` SHALL return the session's messages (role, content, creation time) in chronological order, and an empty list when the session does not exist or belongs to someone else. The model receives at most the 20 most recent messages of the session as conversation history.

#### Scenario: Reopening a conversation
- **WHEN** the user selects a previous conversation
- **THEN** the client SHALL show all its stored messages in order

#### Scenario: Long conversation
- **WHEN** a session has more than 20 messages and the user sends a new one
- **THEN** only the latest 20 messages SHALL be passed to the pipeline as history
