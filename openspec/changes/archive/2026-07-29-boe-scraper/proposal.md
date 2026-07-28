# Proposal: Crear el Scraper del BOE en Go

## Qué queremos construir
Un servicio backend en Go que descargue diariamente los sumarios y documentos XML del BOE, extraiga su contenido, realice un *chunking* semántico y los almacene como Blobs en formato estructurado (JSON). El proyecto debe estar configurado para desarrollo con *hot reload* utilizando `air` y estar orquestado mediante .NET Aspire.

## Por qué es necesario
Este servicio será el motor principal de ingesta de datos de nuestro sistema RAG (Retrieval-Augmented Generation). Utilizar los endpoints XML web directos del BOE garantizará que el texto sea extraído con alta fidelidad y, sobre todo, **con disponibilidad inmediata (datos frescos del día)**, algo que los portales de Datos Abiertos suelen retrasar. Esto resulta en una extracción más estructurada y limpia que si hiciéramos web scraping sobre HTML o PDF.

## Alcance
- **Go + Air**: Inicialización del proyecto en Go con soporte de *hot reload*.
- **.NET Aspire**: Integración del proyecto Go dentro del `AppHost` existente para un arranque unificado.
- **Arquitectura Extensible**: Definición de una interfaz común (`BoletinProvider`) para que, en un futuro cercano, sea sencillo incorporar los scrapers para BOJA y BOPMA compartiendo la lógica core (descarga concurrente, *chunking* y almacenamiento).
- **Scraper BOE**: Implementación completa del consumo de los endpoints XML diarios directos y del XML individual, garantizando la frescura de los datos.
