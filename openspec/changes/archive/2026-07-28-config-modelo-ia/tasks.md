# Tasks: Configuración del Modelo de IA

## Tareas Frontend
- [x] 1. Crear el componente `SettingsPanel.razor` (o un modal) con diseño consistente.
- [x] 2. Implementar formulario para seleccionar Proveedor (Ollama, OpenAI, etc.), Modelo y Precisión (Temperature).
- [x] 3. Implementar input seguro para guardar/actualizar la API Key.
- [x] 4. Crear vista para listar las fuentes de datos (knowledge base) conectadas.
- [x] 5. Conectar el formulario con un nuevo servicio `SettingsSignalRService` o endpoints de Minimal API para cargar y guardar los cambios.

## Tareas Backend (Datos)
- [x] 6. Crear la entidad `AiConfiguration` (ligada al `TenantId` o `UserId`).
- [x] 7. Crear el `AiConfigurationService` para CRUD de la configuración.
- [x] 8. Configurar `IDataProtector` para cifrar y descifrar la API Key en la base de datos de forma segura.
- [x] 9. Actualizar `AppDbContext` y crear/aplicar la migración EF Core para la nueva tabla.

## Tareas Backend (Lógica de IA)
- [x] 10. Exponer endpoints (SignalR o Minimal API) para obtener/actualizar la configuración.
- [x] 11. Refactorizar `AiChatService` para obtener dinámicamente el `AiConfiguration` del tenant actual.
- [x] 12. Modificar la instanciación de Semantic Kernel para construir el cliente adecuado en tiempo de ejecución (ej. usando `.AddOpenAIChatCompletion` vs `.AddOllamaChatCompletion`) en función de la configuración guardada, aplicando la temperatura y la clave correspondientes.
