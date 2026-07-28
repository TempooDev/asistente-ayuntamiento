# Especificación del Cambio: Módulo de Extracción de Datos del BOE (XML)

## 1. Resumen Ejecutivo
El presente documento define la arquitectura y especificaciones técnicas para el desarrollo del nuevo módulo de extracción de datos del Boletín Oficial del Estado (BOE). Este componente forma parte del pipeline de ingesta de datos para un sistema de Inteligencia Artificial basado en arquitectura RAG (Retrieval-Augmented Generation). El objetivo principal es descargar, parsear, estructurar y almacenar de forma diaria las publicaciones del BOE consumiendo directamente sus endpoints XML públicos. Esto asegurará la alta fidelidad del texto y una categorización precisa, evitando los problemas comunes derivados del web scraping de HTML o la lectura de PDFs.

## 2. Alcance
**Incluido:**
* Consumo diario del endpoint XML del sumario del BOE.
* Extracción de identificadores únicos de los documentos publicados.
* Descarga del contenido XML individual de cada documento identificado.
* Parseo y extracción de metadatos específicos y texto plano completo.
* Generación de un archivo JSON estructurado de salida (documento + metadatos).
* Almacenamiento persistente en formato Blob para que posteriormente .NET y Semantic Kernel asuman la carga del *chunking* y los embeddings.

**Fuera de alcance:**
* Extracción mediante scraping de HTML o procesamiento de PDFs.
* Procesamiento de otros boletines en esta fase inicial (BOJA, BOPMA).
* Procesos de Chunking semántico o generación de embeddings (estas tareas se delegan al orquestador .NET con Semantic Kernel en un proceso batch y guardado en pgvector).

## 3. Diseño de la Solución
El flujo de trabajo automatizado (mediante un cron job o planificador) seguirá los siguientes pasos:
1. **Obtención del Sumario:** El servicio de Go realizará una petición HTTP GET al endpoint del sumario utilizando la fecha actual.
2. **Extracción de IDs:** Mediante un parser XML (ej. `encoding/xml` de Go), se iterará sobre el sumario para extraer todos los identificadores de documentos (ej. `BOE-A-YYYY-XXXXX`).
3. **Descarga Concurrente (Controlada):** Por cada identificador extraído, se realizará una petición GET al endpoint del documento individual. Se debe implementar concurrencia mediante Goroutines y Channels, limitando el rate de peticiones para no saturar el servidor del BOE.
4. **Parseo y Limpieza:** El XML descargado se parsea para extraer las etiquetas requeridas y normalizar el contenido del nodo `<texto>`.
5. **Serialización a JSON:** El texto plano junto con la información global del documento se empaquetarán en una estructura JSON.
6. **Almacenamiento en Blob Storage:** El JSON final se almacenará en el Blob Storage para que un proceso batch en .NET (Semantic Kernel) se encargue del chunking y de guardarlo en pgvector.

## 4. Detalles de la API y Endpoints

### 4.1. Endpoint de Sumario Diario (Web XML Directo)
* **Ruta:** `https://www.boe.es/diario_boe/xml.php?id=BOE-S-YYYYMMDD`
* **Método:** `GET`
* **Parámetros:** `id` con el formato `BOE-S-YYYYMMDD` (donde YYYYMMDD representa la fecha requerida).
* **Nota de Arquitectura:** Se utiliza este endpoint web directo en lugar de la API de Datos Abiertos (`/datosabiertos/api/boe/sumario/`) porque garantiza la disponibilidad inmediata (freshness) de los datos publicados en el día, requisito indispensable para el sistema RAG.
* **Respuesta Esperada:** Documento XML conteniendo el índice del día. Se deberán parsear las etiquetas correspondientes a los identificadores del documento.

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
  "text": "Artículo 1. Objeto y ámbito de aplicación...\n\nArtículo 2..."
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
