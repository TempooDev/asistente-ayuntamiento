# Design: Configuración del Modelo de IA

## Arquitectura

### 1. Interfaz de Usuario (Frontend Blazor)
- **SettingsPanel.razor**: Nueva página o modal de configuración accesible desde el menú principal.
- **Pestaña "Modelo de IA"**:
  - Dropdown para seleccionar el proveedor/modelo (Local Ollama, OpenAI, Anthropic, etc.).
  - Input para la API Key (enmascarado tipo `password`).
  - Slider para la "Precisión" (Temperature 0.0 - 1.0).
- **Pestaña "Fuentes Conectadas"**:
  - Lista de fuentes de datos/documentos activos para RAG.

### 2. Backend (API & Base de Datos)
- **Entidad `AiConfiguration`**:
  - Almacenar configuración de IA (Modelo, Provider, Temperature) asociada al `TenantId`.
  - Almacenar la API Key de forma cifrada en la base de datos (usando `IDataProtectionProvider` de ASP.NET Core).
- **Endpoint GET/PUT `/api/settings/ai`** (o vía SignalR):
  - Recuperar configuración (enmascarando la API key en la lectura).
  - Guardar nueva configuración y cifrar la clave.
- **AiChatService Refactor**:
  - Modificar `AiChatService` para que consulte la configuración del usuario/tenant antes de lanzar la petición.
  - Instanciar dinámicamente el `IChatCompletionService` (Semantic Kernel) según el proveedor elegido, inyectando la API Key desencriptada.

### 3. Seguridad
- Las API Keys NUNCA deben enviarse al frontend en texto plano después de guardarse. El GET solo devuelve un indicador booleano de si hay clave guardada, o los últimos 4 dígitos.
- Cifrado en reposo para las claves guardadas en la base de datos.
