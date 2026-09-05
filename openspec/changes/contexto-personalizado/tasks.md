## 1. Base de Datos y Backend

- [x] 1.1 Crear la entidad `UserPreference` en EF Core (TenantId, Topics, Locations) y añadir el `DbSet` al `AppDbContext`.
- [x] 1.2 Crear migración de EF Core para la nueva tabla y aplicarla.
- [x] 1.3 Crear DTOs (`UserPreferenceDto`) y endpoints de API (`GET /api/preferences`, `PUT /api/preferences`) en `ApiService`.
- [x] 1.4 Implementar el servicio `UserPreferenceService` para gestionar las preferencias.

## 2. Inyección de Contexto en RAG

- [x] 2.1 Modificar `AiChatService.cs` para recuperar las preferencias del usuario actual al inicio de `GetCompletionAsync` y `GetStreamingCompletionAsync`.
- [x] 2.2 Inyectar las preferencias en el `systemPrompt` (si existen) asegurando que el modelo las prioriza sin perder de vista los documentos RAG.

## 3. Extracción de Historial (Worker)

- [x] 3.1 Crear un endpoint o servicio `HistoryAnalyzerService` que obtenga las últimas N sesiones de un usuario desde la BD.
- [x] 3.2 Implementar un prompt con Semantic Kernel para extraer `Topics` y `Locations` desde una lista de mensajes.
- [x] 3.3 Guardar/Actualizar los resultados extraídos en `UserPreferences` de forma idempotente (mezclando con las existentes).

## 4. Frontend

- [x] 4.1 Crear un nuevo servicio en Angular (`user-preferences.ts`) para comunicarse con los endpoints de API.
- [x] 4.2 Añadir una nueva sección en la vista de Perfil/Configuración para mostrar los temas de interés y zonas geográficas.
- [x] 4.3 Añadir controles (input, chips) para que el usuario pueda añadir/borrar preferencias de forma manual y un botón de "Analizar mi historial" para lanzar la tarea asíncrona.
