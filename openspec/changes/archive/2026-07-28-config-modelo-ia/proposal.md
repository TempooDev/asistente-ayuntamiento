# Proposal: Configuración del Modelo de IA

## What
Crear una nueva sección de configuración en la webapp donde los usuarios puedan:
1. Seleccionar el modelo de IA que desean utilizar (ej. llama3.2, GPT-4, etc.).
2. Dar de alta API Keys para integrar sus propios LLMs externos.
3. Ajustar parámetros del modelo como la "precisión" (temperature).
4. Ver y gestionar las fuentes de datos (knowledge base) que tienen conectadas.

## Why
Actualmente, la aplicación utiliza un modelo estático configurado en el backend (Ollama `llama3.2`). Para hacer la plataforma más versátil y adaptable a las necesidades de distintos usuarios (o tenants), es fundamental permitir la configuración dinámica del proveedor de IA, sus claves y parámetros, así como dar visibilidad sobre qué datos alimentan las respuestas (fuentes conectadas).
