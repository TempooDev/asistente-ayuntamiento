## ADDED Requirements

### Requirement: Almacenamiento de Preferencias del Usuario
El sistema DEBE permitir almacenar las preferencias e intereses del usuario en la base de datos (PostgreSQL), vinculadas a su `TenantId` o `UserId`.

#### Scenario: Visualización y edición en la UI
- **WHEN** el usuario navega a la sección de Configuración o Perfil
- **THEN** el sistema muestra los intereses y zonas geográficas actuales
- **THEN** el usuario puede añadir, editar o eliminar estos intereses explícitamente y guardar los cambios.
