# Specification: Chat UI Connection

## Purpose
Keep the Angular chat page connected to the chat hub (`/hubs/chat`, routed through the YARP Gateway) and responsive: streams of several conversations run in the background without mixing, a dropped connection is recovered when the user sends, and the page shows progress and scrolls without getting in the way.

## Requirements

### Requirement: Authenticated, Lazily Created Connection
The chat page SHALL reach the hub through `ChatService`, which takes its connection from `HubConnectionFactory`. The SignalR library and the connection SHALL be created on the first use (`ensureConnected()`), so pages without real-time features do not load them. Each (re)connection SHALL send a fresh Auth0 access token (`accessTokenFactory`), and the connection SHALL use SignalR automatic reconnection.

#### Scenario: Opening the chat
- **WHEN** the chat page initializes `ChatStore`
- **THEN** the store SHALL connect to `/hubs/chat` with the user's access token
- **AND** it SHALL load the session list and open the most recent conversation, unless a conversation is already cached (returning to the page)

#### Scenario: Concurrent callers
- **WHEN** several callers ask for the connection while it is starting
- **THEN** they SHALL share the same start instead of opening a second connection

#### Scenario: Connection fails at start
- **WHEN** the first connection fails
- **THEN** the connection state SHALL become `disconnected` and the input SHALL say that it will reconnect when a message is sent

### Requirement: Reconnection Before Sending
When the user sends a message and the hub is not connected, `ChatStore` SHALL first try to connect again, showing the `reconnecting` state. The input SHALL only block sending while a connection is being (re)established or while the current conversation is generating an answer.

#### Scenario: Phone was locked
- **WHEN** the connection was lost (for example, the phone was locked) and the user sends a message
- **THEN** the store SHALL re-establish the connection and send the message without a page reload

#### Scenario: Reconnection fails
- **WHEN** the connection cannot be re-established
- **THEN** the user's message SHALL be shown followed by an assistant notice asking to check the connection and send again

### Requirement: Independent Background Streaming
`ChatStore` (a root singleton) SHALL keep the messages of each conversation in `messagesBySession` and track streaming and waiting per session, so answers of several conversations can stream at the same time without mixing. Streams SHALL keep running when the user switches conversation or page, and the streamed text SHALL be applied at most once per animation frame.

#### Scenario: Switching chats during generation
- **WHEN** an answer is streaming in conversation A and the user opens conversation B
- **THEN** the chunks for A SHALL keep updating A's cached messages
- **AND** they SHALL NOT appear in B
- **AND** returning to A SHALL show its current text and generation state without reloading it from the server

#### Scenario: Deleting a conversation that is streaming
- **WHEN** the user deletes a conversation whose answer is still streaming
- **THEN** its stream subscription SHALL be disposed and its cached messages removed

### Requirement: Waiting Indicator and Smart Scrolling
While a conversation waits for the first chunk, the message list SHALL show an animated indicator whose text rotates every 2.5 s through "Procesando mensaje...", "Buscando referencias..." and "Redactando respuesta...". The list SHALL follow new content only while the user is within 150 px of the bottom; sending a message or opening another conversation SHALL always jump to the bottom.

#### Scenario: Waiting for the first chunk
- **WHEN** the user sends a message and no chunk has arrived yet
- **THEN** the rotating indicator SHALL be shown, and it SHALL disappear with the first chunk

#### Scenario: Following the stream
- **WHEN** chunks arrive and the user is near the bottom of the list
- **THEN** the list SHALL scroll down with the new content

#### Scenario: Reading earlier messages
- **WHEN** chunks arrive and the user has scrolled up more than 150 px from the bottom
- **THEN** the list SHALL NOT scroll, so the user can keep reading
