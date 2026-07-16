# Specification: SignalR Chat Integration

## Purpose
Enable low-latency streaming of chat responses and real-time citation metadata delivery using SignalR.

## Requirements

### Requirement: Citation Metadata Dispatch
The system SHALL dispatch source citations to the client before response text starts streaming.

#### Scenario: Transmitting cited sources
- **WHEN** similar document chunks are fetched
- **THEN** the hub SHALL invoke `ReceiveSources` to send the deduplicated citation array to the client prior to streaming LLM text

### Requirement: Real-time Response Streaming
The system SHALL stream the assistant's response character-by-character as it is generated.

#### Scenario: Yielding text stream
- **WHEN** the chat completion stream yields a text chunk
- **THEN** the system SHALL stream that string back to the Blazor client via `IAsyncEnumerable<string>`
- **AND** append it to the viewport in real-time
