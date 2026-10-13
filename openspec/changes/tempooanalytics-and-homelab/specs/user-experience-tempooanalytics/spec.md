# Specification: UX Analytics with TempooAnalytics

## Purpose
Track user experience metrics in the Angular frontend to understand chat performance, user flow, and interaction patterns.

## ADDED Requirements

### Requirement: Frontend UX Auto-Capture
The frontend application SHALL initialize TempooAnalytics to capture page views, clicks, and session durations without collecting PII (Personally Identifiable Information).

#### Scenario: User visits the app
- **WHEN** a user opens the Angular application
- **THEN** TempooAnalytics SHALL automatically record a page view and track session length (if supported, otherwise custom events for routing).

### Requirement: Chat Reaction Time Tracking
The frontend SHALL measure and report the time elapsed between sending a message and receiving the first token (Time To First Token).

#### Scenario: Sending a message
- **WHEN** the user submits a chat message and the system starts generating the response
- **THEN** the frontend SHALL track the latency until the first streamed chunk arrives and send a custom `chat_reaction_time` event to TempooAnalytics.

### Requirement: External Link Tracking
The frontend SHALL track when users click on source citations or external links.

#### Scenario: User clicks a source citation
- **WHEN** a user clicks on a document reference/link in the chat response
- **THEN** the frontend SHALL capture a `link_clicked` event in TempooAnalytics containing the source type (e.g., BOE, BOJA), without logging sensitive search context.
