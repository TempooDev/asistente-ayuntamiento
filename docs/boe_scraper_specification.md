# Especificación del Cambio: Módulo de Extracción de Datos del BOE (XML)

## 1. Resumen Ejecutivo
El presente documento define la arquitectura y especificaciones técnicas para el desarrollo del nuevo módulo de extracción de datos del Boletín Oficial del Estado (BOE). Este componente forma parte del pipeline de ingesta de datos para un sistema de Inteligencia Artificial basado en arquitectura RAG (Retrieval-Augmented Generation). El objetivo principal es descargar, parsear, estructurar y almacenar de forma diaria las publicaciones del BOE consumiendo directamente sus endpoints XML públicos. Esto asegurará la alta fidelidad del texto y una categorización precisa, evitando los problemas comunes derivados del web scraping de HTML o la lectura de PDFs.

## 2. Alcance
**Incluido:**
* Consumo diario del endpoint XML del sumario del BOE.
* Extracción de identificadores únicos de los documentos publicados.
* Descarga del contenido XML individual de cada documento identificado.
* Parseo y extracción de metadatos específicos y texto plano.
* División semántica del texto (chunking) preservando el contexto de los metadatos.
* Generación de un archivo JSON estructurado de salida.
* Almacenamiento persistente en formato Blob (preparación para vectorización e histórico).

**Fuera de alcance:**
* Extracción mediante scraping de HTML o procesamiento de PDFs.
* Procesamiento de otros boletines en esta fase inicial (BOJA, BOPMA).
* Generación de embeddings o ingesta directa en la base de datos vectorial (este módulo finaliza con el guardado del blob en formato JSON).

## 3. Diseño de la Solución
El flujo de trabajo automatizado (mediante un cron job o planificador) seguirá los siguientes pasos:
1. **Obtención del Sumario:** El servicio de Go realizará una petición HTTP GET al endpoint del sumario utilizando la fecha actual.
2. **Extracción de IDs:** Mediante un parser XML (ej. `encoding/xml` de Go), se iterará sobre el sumario para extraer todos los identificadores de documentos (ej. `BOE-A-YYYY-XXXXX`).
3. **Descarga Concurrente (Controlada):** Por cada identificador extraído, se realizará una petición GET al endpoint del documento individual. Se debe implementar concurrencia mediante Goroutines y Channels, limitando el rate de peticiones para no saturar el servidor del BOE.
4. **Parseo y Limpieza:** El XML descargado se parsea para extraer las etiquetas requeridas. El contenido del nodo `<texto>` será tratado para eliminar marcas residuales de maquetación y normalizar el contenido.
5. **Chunking Semántico:** El texto extraído se segmentará en bloques (chunks) basándose preferiblemente en la estructura del documento (artículos, apartados, párrafos lógicos). Cada chunk heredará un objeto con los metadatos extraídos.
6. **Serialización a JSON:** Los chunks resultantes junto con la información global del documento se empaquetarán en una estructura JSON.
7. **Almacenamiento en Blob Storage:** El JSON final (así como el XML original crudo, para permitir reprocesados sin volver a descargar) se almacenará en el servicio de Blob Storage definido, dejándolo disponible para el pipeline de generación de embeddings.

## 4. Detalles de la API y Endpoints

### 4.1. Endpoint de Sumario Diario (API Datos Abiertos)
* **Ruta:** `https://www.boe.es/datosabiertos/api/boe/sumario/{fecha}`
* **Método:** `GET`
* **Parámetros:** `fecha` en formato `YYYYMMDD` (*path parameter*).
* **Respuestas HTTP Esperadas:** 
  * `200`: Documento XML conteniendo el índice del día. Se deberán parsear las etiquetas de identificadores.
  * `400`: Identificador no válido o parámetros incorrectos.
  * `404`: La información solicitada no existe.
  * `500`: Error del servidor.

### 4.2. Endpoint de Documento Individual
* **Ruta:** `https://www.boe.es/diario_boe/xml.php?id={ID}`
* **Método:** `GET`
* **Parámetros:** `id` representando el identificador único del documento extraído del sumario.
* **Respuesta Esperada:** Documento XML estructurado del cual se extraerán, como mínimo, las siguientes etiquetas clave:
  * `<identificador>`: ID único del documento BOE.
  * `<titulo>`: Título descriptivo de la disposición.
  * `<departamento>`: Órgano emisor.
  * `<fecha_publicacion>`: Fecha de publicación en el boletín.
  * `<texto>`: Contenido principal (texto a segmentar).

## 5. Modelo de Datos
La salida del módulo deberá ser un documento JSON con la siguiente estructura exacta por cada documento procesado:

```json
{
  "document_id": "BOE-A-2026-12345",
  "metadata": {
    "source": "BOE",
    "titulo": "Resolución de X de Y...",
    "departamento": "Ministerio de ...",
    "fecha_publicacion": "2026-07-28"
  },
  "chunks": [
    {
      "chunk_id": "BOE-A-2026-12345_chunk_1",
      "chunk_index": 1,
      "metadata_injected": {
        "document_id": "BOE-A-2026-12345",
        "titulo": "Resolución de X de Y...",
        "departamento": "Ministerio de ...",
        "fecha_publicacion": "2026-07-28"
      },
      "text": "Artículo 1. Objeto y ámbito de aplicación. El presente texto establece..."
    },
    {
      "chunk_id": "BOE-A-2026-12345_chunk_2",
      "chunk_index": 2,
      "metadata_injected": {
        "document_id": "BOE-A-2026-12345",
        "titulo": "Resolución de X de Y...",
        "departamento": "Ministerio de ...",
        "fecha_publicacion": "2026-07-28"
      },
      "text": "Artículo 2. Definiciones. A los efectos de esta disposición se entenderá por..."
    }
  ]
}
```

## 6. Consideraciones No Funcionales
* **Manejo de Errores y Reintentos:** Implementar *Exponential Backoff* para peticiones HTTP fallidas (ej. 500, 503, 429). Los documentos que fallen definitivamente tras un límite de reintentos deben registrarse en un log o cola de errores (Dead Letter Queue) para su revisión y reprocesado manual.
* **Limpieza de Texto:** El contenido extraído del nodo `<texto>` debe ser procesado para normalizar espacios en blanco, retirar saltos de línea innecesarios o caracteres de control, asegurando que el texto entregado a los embeddings sea de máxima calidad semántica.
* **Control de Tasa (Rate Limiting):** Aplicar mecanismos limitadores (e.g., `golang.org/x/time/rate`) en el worker pool para no superar un umbral prudente de peticiones por segundo hacia www.boe.es, previniendo bloqueos temporales de IP.
* **Idempotencia:** La arquitectura debe permitir re-ejecutar el pipeline para una fecha ya procesada sin causar duplicidad de datos en la base vectorial final; los blobs en el storage se sobrescribirán de forma segura o se saltarán si ya existen según la política definida.
* **Observabilidad:** El scraper debe emitir logs estructurados indicando métricas clave: total de IDs encontrados, número de XMLs procesados con éxito, número de errores, chunks totales generados y el tiempo total de ejecución.

## 7. Criterios de Aceptación
- [ ] El script/servicio en Go recibe una fecha como parámetro (o toma la fecha actual por defecto) y obtiene el XML del sumario correctamente.
- [ ] El parseador extrae la lista completa y precisa de identificadores del sumario.
- [ ] Las peticiones a los XML individuales se realizan de forma concurrente, con manejo de errores activo y rate limiting.
- [ ] La extracción de los campos `<identificador>`, `<titulo>`, `<departamento>`, `<fecha_publicacion>` y `<texto>` se realiza con éxito, manejando la posibilidad de que alguna etiqueta falte.
- [ ] El texto del documento es segmentado de manera lógica y semántica (chunking), y los metadatos se inyectan dentro de la estructura de cada fragmento.
- [ ] La estructura del JSON resultante cumple fielmente con el "Modelo de Datos" estipulado.
- [ ] Los archivos producidos (XML en crudo y JSON estructurado) son guardados correctamente en la solución de Blob Storage designada.
- [ ] El proceso puede fallar gracefully ante XMLs corruptos, registrando el error sin detener el flujo general de los demás documentos.

## 8. Extensibilidad: Integración con BOJA y BOPMA
Aunque la fase inicial pone foco en el BOE, el diseño del software (mediante interfaces como `BoletinProvider`) debe estar preparado para integrar los siguientes boletines que también disponen de iniciativas de Datos Abiertos:

* **BOPMA (Boletín Oficial de la Provincia de Málaga):** A través del portal de Datos Abiertos de la Diputación, filtrando por formato XML. [Catálogo BOPMA OpenData](https://opendata.malaga.es/tl/dataset/?_tags_limit=0&organization=diputacion&res_format=XML).
* **BOJA (Boletín Oficial de la Junta de Andalucía):** A través del buscador de APIs del portal de Datos Abiertos de la Junta de Andalucía. [Buscador APIs BOJA](https://www.juntadeandalucia.es/datosabiertos/portal/aplicaciones/buscador-apis).

El módulo de Go deberá estructurarse de tal forma que añadir un nuevo scraper de estas plataformas solo requiera implementar los métodos de obtención y parseo específicos, reutilizando todo el pipeline de *chunking* y almacenamiento en Blobs.
