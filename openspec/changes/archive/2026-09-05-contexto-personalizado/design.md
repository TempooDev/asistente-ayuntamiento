## Context

We need to add a "Personalized Context" feature to the chatbot, so it understands user preferences explicitly (set in settings) or implicitly (extracted from chat history). This will be used in the RAG pipeline to give more personalized responses.

## Goals / Non-Goals

**Goals:**
- Extract topics of interest and location preferences from user chat history automatically.
- Allow users to manually specify or override their interests in the UI.
- Incorporate this context automatically into the `AiChatService` system prompt.

**Non-Goals:**
- We are not creating a full long-term memory graph for individual entities (like remembering specific dog's names). Just high-level topics/locations.
- No cross-user analytics dashboard.

## Decisions

- **Storage**: We will add a new table `UserPreferences` in PostgreSQL linked to the `UserProfile` (or `Auth0UserId`). It will store JSON or explicit columns for `Topics` and `Locations`.
- **Extraction Mechanism**: We will use a Background Service (or the existing Worker) to periodically fetch recent chat sessions (or trigger on demand) and use an LLM completion request to summarize topics.
- **RAG Injection**: In `AiChatService.cs`, we will inject a block of text into the `systemPrompt` (e.g., "El usuario está interesado en: [topics]. Su ubicación principal es: [location].") if preferences exist.

## Risks / Trade-offs

- **Risk**: Extraction LLM calls cost money or time.
  - **Mitigation**: Use local Ollama for the background extraction task, or allow the user to trigger it manually to avoid runaway costs. Or do it efficiently by only analyzing the last N sessions.
- **Risk**: Hallucinated preferences.
  - **Mitigation**: Expose the extracted preferences in the UI so the user can edit or delete them.
