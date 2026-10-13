## ADDED Requirements

### Requirement: Mobile Side Navigation Menu
The system SHALL provide an accessible side navigation menu on mobile devices that overlays the main content when activated.

#### Scenario: Opening the mobile menu
- **WHEN** the user is viewing the application on a mobile device
- **AND** the user taps the menu icon in the top navigation bar
- **THEN** a side navigation panel SHALL slide into view
- **AND** a semi-transparent backdrop SHALL appear over the main content.

#### Scenario: Closing the mobile menu
- **GIVEN** the mobile navigation menu is open
- **WHEN** the user taps the semi-transparent backdrop OR taps a navigation link
- **THEN** the side navigation panel SHALL close
- **AND** the backdrop SHALL disappear.
