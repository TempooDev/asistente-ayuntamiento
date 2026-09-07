## 1. Implement InactivityService and Auto Logout

- [ ] 1.1 Create `src/AsistenteAyuntamiento.Angular/src/app/services/core/inactivity.service.ts` with RxJS `fromEvent` logic and a 30-minute timeout.
- [ ] 1.2 Modify `src/AsistenteAyuntamiento.Angular/src/app/app.ts` to inject and initialize `InactivityService`.

## 2. Refactor Mobile Navigation (Sidebar)

- [ ] 2.1 Modify `src/AsistenteAyuntamiento.Angular/src/app/app.ts` by adding the `mobileMenuOpen` signal and the `toggleMobileMenu()` method.
- [ ] 2.2 Modify `src/AsistenteAyuntamiento.Angular/src/app/app.html` to bind the hamburger icon to the `toggleMobileMenu()` method.
- [ ] 2.3 Add a `fixed md:hidden` container div (offcanvas) in `src/AsistenteAyuntamiento.Angular/src/app/app.html` with the same links as the desktop aside, reacting to the `mobileMenuOpen` signal.
- [ ] 2.4 Add a semi-transparent backdrop to close the mobile menu when clicking outside in `app.html`.

## 3. Chat Improvements (Reconnection, UX, Scrolling)

- [ ] 3.1 Modify `src/AsistenteAyuntamiento.Angular/src/app/pages/chat-panel/chat-panel.ts` in the `sendMessage()` function to intercept the disconnected state and perform a transparent `await chatService.connect()`.
- [ ] 3.2 Create a dynamic array of feedback strings and update the HTML `chat-panel.html` in the `isWaitingForResponse()` block to show the animated message instead of the static one.
- [ ] 3.3 Add a `setInterval` in `chat-panel.ts` that starts when sending the message to rotate the active loading string and stops when receiving a response.
- [ ] 3.4 Update `scrollToBottom()` in `chat-panel.ts` to implement `isNearBottom` validation and not force scroll if the user has manually scrolled up.
