## MODIFIED Requirements

### Requirement: Query Expansion
`QueryExpansionService` SHALL ask the ingestion model to translate the citizen's question into a JSON object with `query_lexica` (2 to 4 keywords separated by spaces, without operators or quotes, because it is parsed by `websearch_to_tsquery`), `query_semantica` (a formal phrase for the embedding) and `filtro_municipio` (a municipality or null).

#### Scenario: Expanding a colloquial query
- **WHEN** a user asks "dónde pido la ayuda del alquiler si tengo 24 años"
- **THEN** the service SHALL return the three fields, accepting the JSON inside a code fence and replacing missing queries with the user's text

#### Scenario: Expansion fails
- **WHEN** the model call fails or its answer is not valid JSON
- **THEN** the service SHALL use the user's words joined by spaces as lexical query, the user's question as semantic query and no municipality filter

### Requirement: Parent Resolution for Generation
The system SHALL resolve the retrieved fragments to their parent documents and give the LLM the full text of each distinct parent once.

#### Scenario: Resolving parent context
- **WHEN** the top 5 fragments are retrieved
- **THEN** the system SHALL load them with their `ParentDocument`
- **AND** pass the `FullText` of each distinct parent to the generation service together with the parent's bulletin id (`DocumentId`), title (`NormTitle`), bulletin, issuing body and publication date

#### Scenario: Labelling a parent document in the prompt
- **WHEN** `ClearLanguageGenerationService` builds the prompt for parent `BOE-A-2026-1234`
- **THEN** the document SHALL be introduced by `[Documento: <title> | Boletín: BOE | Referencia: BOE-A-2026-1234 | Organismo: <issuing body> | Fecha: <yyyy-MM-dd> | URL: https://www.boe.es/buscar/doc.php?id=BOE-A-2026-1234]`, with the same public URL as the flat pipelines (`FlatChunkRagPipeline.GetPublicUrl`)
- **AND** the database id of the parent SHALL NOT be used as its label

#### Scenario: Labelling a BOJA document
- **WHEN** the parent is `BOJA-2026-10-1`
- **THEN** its URL SHALL be `https://www.juntadeandalucia.es/boja/2026/10/1`, the page of that document
- **AND** a BOJA id that does not follow `BOJA-YYYY-N-O` SHALL fall back to `https://www.juntadeandalucia.es/eboja.html`
