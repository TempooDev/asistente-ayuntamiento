# Specification: Session Timeout

## Purpose
Close the session after a period without interaction (30 minutes by default), without cutting an answer that is still streaming and also when the phone was locked longer than the timeout.

## Requirements

### Requirement: Session Inactivity Timeout
While the user is signed in, `InactivityService` SHALL watch `mousemove`, `touchstart` and `keydown` on the document (throttled to once per second) and SHALL log the user out after `environment.sessionTimeoutMinutes` (30) without any of them. Logging out SHALL stop the SignalR connections and call the Auth0 logout, returning to the app's origin.

#### Scenario: User is inactive
- **WHEN** the user triggers no keyboard, mouse or touch event for 30 consecutive minutes
- **THEN** the system SHALL log the user out through Auth0 and return to the app, which then asks to sign in

#### Scenario: User interacts with the app
- **WHEN** the user moves the mouse, presses a key or touches the screen
- **THEN** the 30-minute countdown SHALL start again

#### Scenario: User signs out
- **WHEN** the user is no longer authenticated
- **THEN** the service SHALL stop watching and cancel the countdown

### Requirement: Streaming Answers Hold the Session
Work that must not be cut off SHALL take a hold (`hold()`); the countdown SHALL NOT run while any hold is active. `ChatStore` SHALL hold the session while an answer is streaming, and releasing the last hold SHALL count as activity.

#### Scenario: Answer still streaming
- **WHEN** 30 minutes pass while a chat answer is still being streamed
- **THEN** the session SHALL NOT be closed
- **AND** a full 30-minute countdown SHALL start when the answer finishes

### Requirement: Timeout Checked When the Page Returns
Browsers freeze or throttle timers while a tab is hidden or the phone is locked, so the elapsed time since the last activity SHALL also be checked when the page becomes visible again.

#### Scenario: Return after locking the phone
- **WHEN** the page becomes visible after being hidden for longer than the timeout and no hold is active
- **THEN** the system SHALL log the user out immediately

#### Scenario: Return within the timeout
- **WHEN** the page becomes visible before the timeout has elapsed
- **THEN** it SHALL count as activity and the countdown SHALL start again
