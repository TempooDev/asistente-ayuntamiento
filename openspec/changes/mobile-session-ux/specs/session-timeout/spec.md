## ADDED Requirements

### Requirement: Session Inactivity Timeout
The system SHALL monitor user interaction and automatically terminate the session if the user remains inactive for a configured period (30 minutes).

#### Scenario: User is inactive
- **WHEN** the user does not trigger any keyboard, mouse, or touch events for 30 consecutive minutes
- **THEN** the system SHALL automatically invoke the Auth0 logout process
- **AND** redirect the user to the login or home screen.

#### Scenario: User interacts with the app
- **WHEN** the user moves the mouse, presses a key, or touches the screen
- **THEN** the inactivity timer SHALL be reset to 0.
