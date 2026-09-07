## Context

Currently, the Angular SPA has usability and session management issues on mobile devices:
1. The mobile header shows a menu icon, but it lacks implementation. There is no sliding side container (Offcanvas/Sidebar) for mobile devices.
2. Web clients on mobile frequently lose the WebSocket connection (SignalR) when the screen turns off. As a result, when trying to send a message with a closed socket, the function silently returns and the UI appears unresponsive.
3. The chat component, upon receiving a data stream from SignalR, reassigns the scroll to the bottom of the screen on every chunk received. This causes abrupt jumps if the user was trying to read a long text further up.
4. Additionally, the session needs to be closed automatically (Auth0) if the user remains inactive for security reasons.

## Goals / Non-Goals

**Goals:**
- Provide intuitive mobile navigation by reusing the existing structure.
- Transparent and automatic recovery of the WebSocket connection.
- Dynamic animation during the waiting state (Processing).
- "Smart Auto-Scroll" behavior.
- Centralized automatic logout (default 30 minutes).

**Non-Goals:**
- Rewrite the chat architecture.
- Refactor SignalR in the ASP.NET Core backend.

## Decisions

**1. Auto-Reconnect in `sendMessage`**
- *Decision:* Before emitting a message or stream in the chat service, if the connection indicates `isConnected == false`, we will asynchronously invoke `chatService.connect()` before proceeding.
- *Rationale:* Avoids having to implement complex heartbeat policies, solving the problem exactly when the user demonstrates interactive intent.

**2. Smart Scrolling**
- *Decision:* Upon receiving each chunk of the stream, we will calculate: `isNearBottom = (element.scrollHeight - element.scrollTop - element.clientHeight) < 150`. If true, we scroll to the bottom. Otherwise, we skip it.
- *Rationale:* Provides the same modern functionality as native applications like ChatGPT, without relying on third-party libraries.

**3. InactivityService**
- *Decision:* A root-level `Injectable` that subscribes to native events (`mousemove`, `touchstart`, `keydown`) using RxJS `fromEvent`, and resets a Subject timer.
- *Rationale:* It is the standard pattern in Angular for inactivity detection, avoiding memory leaks or excessive calls to change detection (NgZone).

**4. Dynamic Loading Feedback**
- *Decision:* We will use an interval in Angular (via the Signals API or a managed `setInterval`) to iterate over predefined messages while `isWaitingForResponse()` is true.

## Risks / Trade-offs

- **Risk:** `fromEvent` in the `InactivityService` could degrade performance if it triggers Angular change detection continuously.
  - *Mitigation:* We will use `runOutsideAngular` for listeners and RxJS operators like `throttleTime` or `debounceTime` to reduce events to a maximum of 1 per second.
- **Risk:** SignalR reconnection might take a few seconds.
  - *Mitigation:* The UI will indicate that the reconnection action is in progress before reactivating the stream.
