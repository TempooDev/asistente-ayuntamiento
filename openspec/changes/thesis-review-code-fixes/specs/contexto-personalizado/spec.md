## MODIFIED Requirements

### Requirement: Preferences in the Answers
The baseline pipeline (`BaselineRagPipeline`) SHALL append the user's topics and locations to its system prompt (`PromptCatalog.UserContext`) when the history has no system message yet and documents were retrieved (`PromptCatalog.StreamingSystem`); without documents it uses `PromptCatalog.ConversationalSystem` without preferences. A user without preferences SHALL get no user-context block. The hierarchical pipeline does not use preferences.

#### Scenario: User interested in housing in Málaga
- **WHEN** a user with topic "Vivienda" and location "Málaga" streams a question that retrieves documents
- **THEN** the system prompt sent to the model SHALL mention both

#### Scenario: Small talk
- **WHEN** the same user streams a message that retrieves no documents
- **THEN** the system prompt SHALL be the conversational one, without the user-context block
