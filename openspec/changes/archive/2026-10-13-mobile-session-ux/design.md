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
- *Decision:* A root-level `Injectable` in `core/auth/inactivity.service.ts` that subscribes to native events (`mousemove`, `touchstart`, `keydown`, `visibilitychange`) using RxJS `fromEvent` + `throttleTime(1000)`, and resets a timer.
- *Logout:* through `AuthFacade.logout()`, created in `audit-remediation-2026-10` task 7.2. If that task is not done yet, call `AuthService.logout()` directly and stop the SignalR hubs first.
- *Rationale:* The app is **zoneless** (no zone.js), so DOM listeners do not trigger change detection, and `runOutsideAngular` is not needed. Only the logout path touches signals.
- *Timeout:* configured in `environment` (`sessionTimeoutMinutes`, default 30), not hardcoded.

**5. Dependency on audit-remediation-2026-10 (revised 2026-10-13)**
- This change is implemented **after phase 2** of `audit-remediation-2026-10`.
- Task 2.6 of that change creates `core/realtime/hub-connection.factory.ts`, with an idempotent `connect()`, a token refreshed on every reconnection, and hubs stopped on logout.
- Decision 1 (auto-reconnect in `sendMessage`) therefore reduces to calling `chatService.ensureConnected()` from the factory before sending. No custom reconnection logic is added here.

**4. Dynamic Loading Feedback**
- *Decision:* We will use an interval in Angular (via the Signals API or a managed `setInterval`) to iterate over predefined messages while `isWaitingForResponse()` is true.

## Risks / Trade-offs

- **Risk:** high-frequency `fromEvent` listeners (`mousemove`) waste CPU.
  - *Mitigation:* `throttleTime(1000)`. The app is zoneless, so there is no change-detection cost.
- **Risk:** logout while a chat stream is in progress loses the partial answer.
  - *Mitigation:* the timer only counts while there is no active stream (`ChatStore.isGenerating()` / `chat-panel.isGenerating()`).
- **Risk:** SignalR reconnection might take a few seconds.
  - *Mitigation:* The UI will indicate that the reconnection action is in progress before reactivating the stream.
