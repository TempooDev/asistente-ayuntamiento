# Specification: Mobile Navigation

## Purpose
Give phone users the same navigation as the desktop sidebar: an accessible off-canvas menu opened from the header, with links filtered by role, that closes on navigation, Escape or the backdrop.

## Requirements

### Requirement: Mobile Side Navigation Menu
Below the `md` breakpoint the app shell SHALL show a top bar with a menu button (only for signed-in users) instead of the desktop sidebar. The button SHALL open a side panel over the content, shown at once without a slide animation, with a semi-transparent backdrop. The panel SHALL be a modal dialog (`role="dialog"`, `aria-modal="true"`) that moves the focus to its first control, keeps Tab inside it and returns the focus to the menu button when it closes; the button SHALL expose `aria-expanded` and `aria-controls`.

#### Scenario: Opening the mobile menu
- **WHEN** a signed-in user on a narrow screen taps the menu icon in the top bar
- **THEN** the side navigation panel SHALL appear with a semi-transparent backdrop over the main content
- **AND** the focus SHALL move into the panel

#### Scenario: Closing the mobile menu
- **WHEN** the menu is open and the user taps the backdrop, the close button or a link, presses Escape, or a navigation ends
- **THEN** the panel and the backdrop SHALL disappear

### Requirement: Same Links as the Desktop Sidebar
The mobile menu SHALL list the same links as the desktop sidebar, filtered by role with the same rules: Chat and Perfil for every signed-in user, Documentos for `admin-plataforma`, Arena (A/B) for `admin-organizacion`, Configuración and Métricas for either admin role. It SHALL also offer "Cerrar Sesión".

#### Scenario: Regular user
- **WHEN** a user without admin roles opens the menu
- **THEN** only Chat, Perfil and "Cerrar Sesión" SHALL be shown

#### Scenario: Organization administrator
- **WHEN** a user with `admin-organizacion` opens the menu
- **THEN** Arena (A/B), Configuración and Métricas SHALL be shown in addition, but not Documentos
