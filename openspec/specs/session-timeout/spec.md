# session-timeout Specification

## Purpose
Close the session after a period without interaction (30 minutes by default), without cutting an answer that is still streaming and also when the phone was locked longer than the timeout.
## Requirements
### Requirement: Session Inactivity Timeout
The system SHALL monitor user interaction and automatically terminate the session if the user remains inactive for a configured period (30 minutes).

#### Scenario: User is inactive
- **WHEN** the user does not trigger any keyboard, mouse, or touch events for 30 consecutive minutes
- **THEN** the system SHALL automatically invoke the Auth0 logout process
- **AND** redirect the user to the login or home screen.

#### Scenario: User interacts with the app
- **WHEN** the user moves the mouse, presses a key, or touches the screen
- **THEN** the inactivity timer SHALL be reset to 0.

#### Scenario: Answer still streaming
- **WHEN** the 30 minutes elapse while a chat answer is still being streamed
- **THEN** the session SHALL NOT be closed
- **AND** the countdown SHALL restart when the answer finishes

#### Scenario: Return after locking the phone
- **WHEN** the page becomes visible again after being hidden (screen locked, tab in the background) for longer than the timeout
- **THEN** the system SHALL log the user out immediately, even if the browser froze the timer while hidden

