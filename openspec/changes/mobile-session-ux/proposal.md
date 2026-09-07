## Why

The current mobile experience has significant usability issues. The navigation bar shows a menu icon but doesn't actually deploy a sidebar, preventing users from accessing different sections. Additionally, the chat WebSocket connection (SignalR) frequently drops when mobile devices go to sleep, causing the chat interface to become unresponsive without any visual warning. Finally, for security reasons, the user session needs to be closed automatically after a prolonged period of inactivity (e.g., 30 minutes). Users also experience a frozen visual state or abrupt jumping ("scrolling while typing") while receiving the chat response.

## What Changes

- Implement mobile sidebar navigation that slides in when the header menu button is tapped.
- Add a global `InactivityService` to automatically log the user out after 30 minutes with no interaction (keyboard, mouse, or touch).
- Modify the chat service and main panel to intercept disconnections and transparently attempt an `auto-reconnect` right before sending messages, preventing silent failures.
- Incorporate animated, dynamic text messages ("Processing message...", "Searching references...") while waiting for the first chat response chunk.
- Implement 'smart scrolling': it will only force auto-scroll to the bottom (`scrollToBottom`) if the user was already at the bottom of the chat, preventing annoying visual jumps if the user had scrolled up to read previous messages.

## Capabilities

### New Capabilities
- `session-timeout`: Defines the security behavior for automatic session termination when the user is inactive.
- `mobile-navigation`: Updates the overall layout behavior to support functional navigation via a hamburger menu on smartphones.

### Modified Capabilities
- `chat-ui-connection`: Updates requirements related to connection stability on mobile devices and UX improvements when receiving message streams (loading animations, adaptive scrolling).

## Impact

- **UI/Components:** `app.ts`, `app.html`, `chat-panel.ts`, `chat-panel.html`.
- **Services:** `chat.service.ts` will be modified to better tolerate temporary mobile disconnections, and a new `InactivityService` will be added.
- **Security/Auth:** The inactivity detector will be wired to the Auth0 SPA SDK's `auth.logout()`.

## Non-goals

- No major chat UI refactoring will be performed; changes are strictly limited to mobile improvements and auto-scroll.
- No modifications will be made to the backend or the ASP.NET Core SignalR service; reconnection logic will be handled purely on the client side (Angular).
