# Proposal: Vector Ingestion and RabbitMQ Consumer

## Context
Actualmente, nuestro scraper en Go extrae exitosamente documentos de los boletines oficiales (ej. BOE), guarda el texto estructurado en JSON en Azure Blob Storage (Azurite), y publica un evento en RabbitMQ notificando la disponibilidad del nuevo archivo. Sin embargo, esta información permanece estática y no está siendo procesada para permitir búsquedas semánticas (RAG).

## Problem
No existe un flujo que consuma estos eventos de RabbitMQ, procese el texto extraído en los archivos JSON, lo divida en fragmentos (chunks), genere embeddings vectoriales y los guarde en la base de datos (PostgreSQL con pgvector). Esto bloquea el caso de uso central del Asistente del Ayuntamiento: responder preguntas basándose en documentos legales vigentes.

## Proposed Solution
1. **Consumidor en .NET**: Crear un servicio Worker o BackgroundService en .NET (o aprovechar el `ApiService` existente) que se conecte a la cola `documents_to_process` de RabbitMQ.
2. **Carga y Procesamiento**: Al recibir el mensaje, el consumidor descargará el archivo JSON referenciado desde Azurite.
3. **Semantic Kernel**: Utilizar .NET Semantic Kernel para realizar un *Text Chunking* semántico sobre el campo de texto del boletín.
4. **Vectorización y Almacenamiento**: Generar los embeddings (usando Ollama, que ya está configurado en el AppHost con el modelo `llama3.2` o su equivalente local para embeddings) y persistirlos en PostgreSQL haciendo uso de pgvector.

## Value Prop
Completar este flujo permite tener el *RAG (Retrieval-Augmented Generation)* listo de extremo a extremo, desde la recolección del dato público hasta la vectorización semántica lista para ser consultada por el LLM.
