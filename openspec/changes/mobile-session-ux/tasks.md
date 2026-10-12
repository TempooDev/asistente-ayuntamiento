> **Revised 2026-10-13.** Implement after phase 2 of `audit-remediation-2026-10`, which provides the SignalR hub factory (task 2.6). Paths assume the current layout. If phase 7 of the audit (the `core/shared/features` restructure) has already been merged, apply the same changes to the new paths shown in brackets.

## 1. Implement InactivityService and Auto Logout

- [ ] 1.1 Create `src/AsistenteAyuntamiento.Angular/src/app/core/auth/inactivity.service.ts`, using `fromEvent` (`mousemove`, `touchstart`, `keydown`, `visibilitychange`) + `throttleTime(1000)`, with the timeout from `environment.sessionTimeoutMinutes` (default 30). The timer is paused while a chat stream is active.
- [ ] 1.2 Add `sessionTimeoutMinutes` to `src/environments/environment.ts` and `environment.development.ts`.
- [ ] 1.3 Start `InactivityService` in `src/app/app.ts` [`core/layout/shell.component.ts`] only when the user is authenticated. On timeout, stop the hubs and log out (through `AuthFacade` if task 7.2 of the audit exists).
- [ ] 1.4 Spec `inactivity.service.spec.ts` with fake timers: logout after the timeout, reset on interaction, no logout while streaming.

## 2. Mobile Navigation (Sidebar)

- [ ] 2.1 `src/app/app.ts` [`shell.component.ts`]: add the `mobileMenuOpen` signal and `toggleMobileMenu()` / `closeMobileMenu()`. Close the menu on `NavigationEnd`.
- [ ] 2.2 `src/app/app.html:10` [`shell.component.html`]: replace the `<span>` menu icon with a `<button type="button" aria-label="Abrir menú" [attr.aria-expanded]="mobileMenuOpen()">` bound to `toggleMobileMenu()`.
- [ ] 2.3 Add a `fixed md:hidden` off-canvas panel in the same template, reusing the desktop navigation links (extract them to a `@for` over a single `navItems` computed, so the role filtering is not duplicated). Use `role="dialog"`, `aria-modal="true"`, and close it with Escape.
- [ ] 2.4 Add a semi-transparent backdrop that closes the menu on click.

## 3. Chat Improvements (Reconnection, UX, Scrolling)

- [ ] 3.1 `src/app/pages/chat-panel/chat-panel.ts` [`features/chat/chat.store.ts`]: before sending, `await chatService.ensureConnected()` (provided by the hub factory, audit task 2.6). Show a "Reconectando…" state in the input while it resolves. Do not use `isConnected()` as a hard block in `sendMessage()` (`chat-panel.ts:224`).
- [ ] 3.2 `chat-panel.html` [`message-list`]: replace the static waiting text with a rotating message from a constant array (`Procesando mensaje…`, `Buscando referencias…`, `Redactando respuesta…`), with `aria-live="polite"`.
- [ ] 3.3 Drive the rotation with an `interval` that is cleaned up through `takeUntilDestroyed` and stopped on the first chunk. Do not use a raw `setInterval` (see the leak fixed in audit task 7.8).
- [ ] 3.4 `scrollToBottom()` (`chat-panel.ts:214`): only auto-scroll when `scrollHeight - scrollTop - clientHeight < 150`, or when the user just sent a message.
- [ ] 3.5 Manual check on a phone (or Chrome device emulation): the menu works, the screen is locked for 1 minute and the next message still sends, and scrolling up during streaming does not jump.
