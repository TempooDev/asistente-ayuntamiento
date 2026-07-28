# Proposal: Add Ollama Chat Integration

## What
Implementar la conexión del chat con un proveedor de IA utilizando Ollama, específicamente con el modelo `llama3`, aprovechando la integración de Ollama para .NET Aspire. El sistema permitirá a los usuarios enviar y recibir mensajes, manteniendo el contexto (histórico) de la conversación.

## Why
Para proporcionar a los usuarios una experiencia conversacional fluida e inteligente, es fundamental contar con un modelo de lenguaje. Al utilizar Ollama localmente (con `llama3`), podemos probar e iterar rápidamente sin depender de APIs de terceros o incurrir en costos durante el desarrollo. Además, el manejo del histórico permitirá conversaciones más naturales y coherentes.

## Features
- **Integración con Ollama:** Conexión con `llama3` mediante Aspire.
- **Histórico de Conversación:** Almacenamiento y recuperación de mensajes de la sesión actual y pasadas.
- **Retención de Datos:** Solo se mantendrán los históricos de conversaciones de menos de una semana de antigüedad.
- **Compactación de Contexto:** Reglas para compactar el histórico cuando este exceda la ventana de contexto del modelo, evitando errores por exceso de tokens y optimizando el rendimiento.

## Non-goals
- No se implementará por ahora conexión con proveedores externos como OpenAI, Gemini o Claude (solo Ollama).
- No se diseñarán UI/UX complejas para la gestión del histórico más allá de lo necesario para el funcionamiento del chat.
- No se implementarán modelos de embeddings en esta fase, esto es puramente para generación de texto/chat.
