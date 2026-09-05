# Specification: Procesamiento Histórico

## Purpose
TBD (Extracción automática de intereses desde el historial de chat)

## Requirements

### Requirement: Extracción asíncrona de intereses
El sistema DEBE proveer un mecanismo (tarea en segundo plano o endpoint a demanda) para analizar el historial de chat de un usuario y extraer temas de interés recurrentes y ubicaciones.

#### Scenario: Análisis de chats recientes
- **WHEN** se dispara la tarea de análisis para un usuario
- **THEN** el sistema recupera sus mensajes recientes y utiliza un LLM para identificar palabras clave, temas y municipios de interés
- **THEN** los nuevos intereses se fusionan con los existentes en su perfil de preferencias.
