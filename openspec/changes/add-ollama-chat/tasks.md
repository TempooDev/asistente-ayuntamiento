# Tasks: Add Ollama Chat Integration

## 1. Setup Aspire AppHost for Ollama `llama3`
**File:** `src/AsistenteAyuntamiento.AppHost/Program.cs`
- Localizar la definición del recurso de Ollama (si ya existe para `nomic-embed-text`) y añadir explícitamente `.AddModel("llama3")`.
- Pasar la referencia de este modelo de chat al proyecto `ApiService`.

## 2. Configurar Chat LLM en ApiService mediante Semantic Kernel
**File:** `src/AsistenteAyuntamiento.ApiService/Program.cs`
- Utilizar explícitamente **Semantic Kernel** para la integración.
- Registrar el `IChatCompletionService` usando el builder de Semantic Kernel (`AddOllamaChatCompletion`) apuntando a Ollama y al modelo `llama3`.
- Asegurarse de que utilice el fallback a Ollama local (como dictan las reglas del proyecto) en la configuración del Kernel.

## 3. Lógica del Historial de Chat y Retención (< 1 semana)
**Files:** Repositorios de datos en `src/AsistenteAyuntamiento.ApiService` o `Infrastructure`.
- Implementar métodos para recuperar los mensajes anteriores de una sesión de chat (`ChatSessions` y `ChatMessages`).
- Filtrar la consulta para ignorar mensajes de más de 7 días (o bien implementar un mecanismo de limpieza).
- **Importante:** Asegurarse de que las consultas filtren siempre por `userId` para mantener la privacidad.

## 4. Implementar Mecanismo de Compactación
**File:** Servicio de aplicación (ej. `ChatService.cs` en `Application` o `ApiService`).
- Antes de enviar el historial recuperado a Ollama, aplicar una regla de compactación (ej. truncar los mensajes más antiguos manteniendo los últimos `N` turnos) si el historial es demasiado largo.
- Asegurarse de conservar el `SystemPrompt`.

## 5. Orquestar Chat a través del Hub de SignalR
**File:** `src/AsistenteAyuntamiento.ApiService/Features/Chat/ChatHub.cs` o `Hubs/ChatHub.cs`.
- Modificar el método `SendMessage` o `StreamChat` del hub de SignalR existente.
- Construir un objeto `ChatHistory` de Semantic Kernel a partir del historial procesado (aplicando la compactación) y añadir el nuevo mensaje.
- Invocar a `IChatCompletionService.GetChatMessageContentAsync` (o su versión en streaming) pasando el `ChatHistory`.
- Guardar el nuevo mensaje del usuario y la respuesta generada en la base de datos, y enviar la respuesta al cliente por SignalR.

## 6. Integración en Frontend (Blazor)
**Files:** Componentes en `src/AsistenteAyuntamiento.Web` y `src/AsistenteAyuntamiento.Web.Client` (`ChatPanel.razor`, `ChatSignalRService.cs`).
- Asegurar que la interfaz de usuario para el chat envíe y reciba los mensajes usando el servicio SignalR actual.
- Mostrar el historial.
