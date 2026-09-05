## Why

A medida que los usuarios utilizan el asistente, acumulan un historial de interacciones que revela las áreas temáticas (por ejemplo, urbanismo, servicios sociales) o zonas geográficas por las que más consultan. Actualmente, cada chat comienza sin contexto previo, lo que obliga al usuario a repetir información o al asistente a proporcionar respuestas demasiado genéricas. Este cambio resolverá ese problema extrayendo un contexto genérico del historial de usuario y permitiendo al usuario declarar sus propias preferencias explícitas, lo que permitirá enfocar y personalizar mucho mejor las respuestas del agente.

## What Changes

- Se añadirá una funcionalidad para procesar periódicamente o bajo demanda el historial de chat de un usuario y extraer temas de interés y preferencias.
- Se creará una sección en la interfaz de usuario donde el usuario podrá ver, editar y añadir explícitamente sus preferencias e intereses.
- Se modificará el motor de RAG (`AiChatService`) para inyectar este "contexto de usuario" de forma dinámica y prioritaria en el prompt del sistema al generar respuestas.
- Se implementará un almacenamiento estructurado para estas preferencias (probablemente en PostgreSQL) vinculado al perfil del usuario.

## Capabilities

### New Capabilities
- `contexto-personalizado`: Gestión del contexto de preferencias del usuario, ya sean inferidas a partir del historial o declaradas explícitamente en la interfaz de usuario.
- `procesamiento-historico`: Trabajo en segundo plano o asíncrono para resumir o extraer entidades e intereses a partir de las sesiones pasadas del usuario utilizando el LLM.

### Modified Capabilities
- `asistente-rag`: Se modificará la generación del prompt para inyectar las preferencias activas del usuario, influyendo en cómo el modelo selecciona y explica la información de los boletines oficiales.

## Impact

- **Backend**: `AiChatService` deberá recuperar las preferencias del usuario. Habrá nuevos endpoints para gestionar el perfil del usuario. Podría ser necesario un trabajo en segundo plano para procesar sesiones de chat antiguas (ej. en `Worker`).
- **Base de Datos**: Nuevas tablas o columnas asociadas al usuario para almacenar las preferencias estructuradas.
- **Frontend**: Nueva pantalla o sección en "Configuración" (o perfil de usuario) para visualizar y editar intereses declarados.
- **LLM**: El system prompt recibirá instrucciones adicionales basadas en el contexto del usuario.

## Non-goals

- No se pretende que el asistente recuerde conversaciones pasadas como un historial infinito (memoria a largo plazo a nivel de mensaje), sino únicamente extraer preferencias genéricas de alto nivel (temas de interés, zonas geográficas).
- No se creará un panel de administración complejo para analizar tendencias de todos los usuarios; es una funcionalidad centrada únicamente en mejorar la experiencia individual.
