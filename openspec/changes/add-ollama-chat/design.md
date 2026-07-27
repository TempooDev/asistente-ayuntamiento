# Design: Add Ollama Chat Integration

## System Architecture

1. **.NET Aspire Integration (`AsistenteAyuntamiento.AppHost`)**
   - Integrar el recurso de Ollama y añadir explícitamente el modelo `llama3`.
   - Inyectar el endpoint de Ollama en `ApiService`.

2. **Backend API (`AsistenteAyuntamiento.ApiService`)**
   - Configurar la integración explícitamente mediante **Semantic Kernel**, registrando Ollama como el `IChatCompletionService` principal para esta feature.
   - Utilizar el objeto `ChatHistory` de Semantic Kernel para orquestar la conversación.
   - Actualizar el Hub de SignalR existente (`ChatHub`) para gestionar la comunicación en tiempo real.
   - El Hub requerirá extraer el `userId` del contexto de SignalR (obtenido vía token Auth0) para garantizar el aislamiento de datos.

3. **Gestor del Historial (Contexto de Conversación)**
   - Utilizar las tablas `ChatSessions` y `ChatMessages` para almacenar cada interacción.
   - Al recibir un nuevo mensaje, recuperar los mensajes anteriores de la sesión actual.
   - **Retención de 1 Semana:** Modificar la consulta a la base de datos o añadir un job en background para excluir u borrar mensajes/sesiones con más de 7 días de antigüedad.
   - **Estrategia de Compactación:**
     - Establecer un límite máximo de tokens o de historial (ej. mantener solo los últimos N mensajes, o calcular el tamaño en caracteres aproximado de los últimos mensajes).
     - Si el historial excede la ventana de contexto, descartar los mensajes más antiguos de la sesión (algoritmo FIFO de contexto) antes de enviarlos a Ollama, manteniendo siempre el mensaje inicial/system prompt intacto.

4. **Frontend (`AsistenteAyuntamiento.Web` / Blazor)**
   - Actualizar o crear el componente de chat para enviar los mensajes al nuevo endpoint y renderizar la respuesta del agente.
   - Mostrar el histórico de la sesión actual recuperado al cargar el componente.

## Data Models (if changes needed)
- `ChatMessage`: Debe tener un campo `CreatedAt` y pertenecer a un `SessionId`, además del `UserId` (heredado de la sesión o explícito) para poder limpiar mensajes viejos.

## Edge Cases & Limitations
- **Token limit:** Aunque hay compactación, mensajes individuales muy largos podrían causar problemas. Se puede aplicar un límite de longitud al input del usuario.
- **Ollama Initialization:** En dev, el primer arranque puede ser lento mientras se descarga `llama3`.
