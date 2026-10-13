# Specification: Hierarchical Parent-Child Retrieval with Hybrid Search

## Purpose
Provide the hierarchical RAG pipeline next to the baseline one: documents are stored as one parent with enriched child fragments, queries are expanded by an LLM, child fragments are retrieved with hybrid search (Qdrant dense ranking + PostgreSQL full-text ranking fused with RRF), and the answer is generated in plain language from the full text of the parent documents.

## Requirements

### Requirement: Parent-Child Document Schema
The hierarchical pipeline SHALL store each document as one `ParentDocument` (unique `DocumentId`) whose `FullText` is the whole text of the scraper's JSON document, plus `ChildFragments` rows (one per fragment) that reference it through `ParentId` with `ON DELETE CASCADE`. The parent SHALL carry `NormTitle` (the document title, or the document id when there is none), `IssuingBody` (the department), `PublicationDate` and `Bulletin`.

#### Scenario: Ingesting a BOE document
- **WHEN** `BoeIngestionService` processes `BOE-A-2026-1234`
- **THEN** it SHALL read the metadata and text from the JSON blob of the message and the structure from `raw-xml/BOE/BOE-A-2026-1234.xml`
- **AND** split the text of each `<articulo>` (or the whole `<texto>` when there is no `<articulo>`) with the configured `IChunkingStrategy`, one fragment per chunk, sectioned by the article id and, for split articles, with sub-section `parte N`

#### Scenario: BOE document without raw XML
- **WHEN** `raw-xml/BOE/{id}.xml` does not exist (the scraper stores it only as a best-effort backup)
- **THEN** `BoeIngestionService` SHALL log a warning and split the `text` of the JSON document as a single article with id `1`
- **AND** the ingestion SHALL NOT fail because of the missing XML

#### Scenario: Ingesting a BOJA document
- **WHEN** `BojaIngestionService` processes a BOJA document
- **THEN** it SHALL split the document text with the configured `IChunkingStrategy`, one fragment per chunk with section `Sección N`
- **AND** use "Junta de Andalucía" as issuing body when the document has no department

### Requirement: Document Municipality
Both ingestion pipelines SHALL derive the municipality of a document with `MunicipalityExtractor` (Application, no LLM) from the scraper metadata: from `departamento` when it is an Ayuntamiento ("AYUNTAMIENTO DE VÉLEZ-MÁLAGA", the BOE section V-B case) and otherwise from the issuer clause of `titulo`, the one right after the date ("Anuncio de 3 de febrero de 2026, del Ayuntamiento de Sedella, de …") or right after the document type ("Anuncio del Ayuntamiento de Ronda por el que …"). The display name SHALL be in title case with accents kept, connectors ("de", "la", "del", "los"…) in lower case and the province in parentheses removed. A document SHALL get a municipality only when its issuer is a single Ayuntamiento; otherwise (state or regional bodies, Diputación Provincial, Mancomunidad, Consorcio, municipal agencies, several Ayuntamientos, an Ayuntamiento only mentioned in the subject) the municipality MUST stay null, because a document without municipality applies to every municipality while a wrong one would hide it.

The municipality filter compares a match key (`MunicipalityExtractor.ToMatchKey`): lower case, without accents, punctuation as spaces and without a leading "Ayuntamiento de" or a province in parentheses. `ParentDocument.Municipality` SHALL keep the display name; `ChildFragment.Municipality` and the `Municipality` payload of both Qdrant collections SHALL hold the match key. Documents ingested before this behaviour have no municipality until they are re-ingested (admin reprocess).

#### Scenario: BOE document issued by an Ayuntamiento
- **WHEN** a BOE document with `departamento` "AYUNTAMIENTO DE VÉLEZ-MÁLAGA" is ingested
- **THEN** its `ParentDocument.Municipality` SHALL be "Vélez-Málaga"
- **AND** its fragments and the points of both collections SHALL carry the key "velez malaga"

#### Scenario: Ayuntamiento in the title
- **WHEN** a document titled "Resolución de 3 de marzo de 2026, del Ayuntamiento de Alhaurín de la Torre (Málaga), referente a la convocatoria …" is ingested, or a BOJA document (department "Junta de Andalucía") titled "Anuncio de 3 de febrero de 2026, del Ayuntamiento de Sedella, de aprobación inicial …"
- **THEN** its municipality SHALL be "Alhaurín de la Torre" or "Sedella" respectively

#### Scenario: Document not issued by one Ayuntamiento
- **WHEN** the issuer is a ministry, the Junta de Andalucía, a Diputación Provincial, a Mancomunidad or a Consorcio, or the title only mentions an Ayuntamiento after the issuer ("…, de la Dirección General de Administración Local, por la que se admite la inscripción del escudo del Ayuntamiento de Sedella")
- **THEN** the document's municipality SHALL be null and its payload `Municipality` empty

### Requirement: Unified Hierarchical Ingestion Pipeline
The system SHALL run the ingestion of every bulletin through the abstract `BaseHierarchicalIngestionProcessor` (`Application.Features.Ingestion`), whose subclasses only extract the parent and the raw fragments.

#### Scenario: Common pipeline execution
- **WHEN** a bulletin processor processes a document
- **THEN** it SHALL enrich every fragment and generate the embeddings (batches of 100) before writing anything
- **AND** replace the document's previous `ParentDocument` and fragments in one PostgreSQL transaction
- **AND** after the commit, delete the document's previous points from the Qdrant collection `child_fragments` and upsert the new ones
- **AND** record an `IngestionMetrics` row with `PipelineType.Hierarchical`

### Requirement: Only Bulletins in Scope
The hierarchical pipeline SHALL have a keyed processor only for the bulletins in the project's scope (BOE, BOJA).

#### Scenario: Message from another source
- **WHEN** a message whose source has no processor reaches `HierarchicalIngestionConsumer`
- **THEN** it SHALL be sent to `documents_to_process_hierarchical.dlq` without retries and the document's hierarchical status set to `Failed`
- **AND** nothing SHALL be written to the database or the vector store

### Requirement: Child Fragment Contextual Enrichment
Before embedding, `FragmentEnrichmentService` SHALL build each fragment's `ChunkText` from (1) a breadcrumb `[BOLETÍN: … | ORGANISMO: … | NORMA: … | ARTÍCULO: <fragment section> | SUBSECCIÓN: <fragment sub-section>]`, where the `SUBSECCIÓN` segment MUST be omitted when the fragment has no sub-section, (2) two citizen questions generated by the ingestion model (`Ai:Ingestion:*`, falling back to `Ai:Chat:*`) under `PREGUNTAS SINTÉTICAS:`, and (3) the original text under `CONTENIDO ORIGINAL:`.

#### Scenario: Generating enriched fragment text
- **WHEN** the second part of BOE article `a5`, split by the chunking strategy, is enriched
- **THEN** its `ChunkText` SHALL start with a breadcrumb ending in `ARTÍCULO: a5 | SUBSECCIÓN: parte 2]`
- **AND** contain the two generated questions and the original text
- **AND** the stored `ChildFragments.SubSection` SHALL be `a5 (parte 2)`

#### Scenario: Fragment without sub-section
- **WHEN** an unsplit BOE article or a BOJA section (`Sección N`) is enriched
- **THEN** its breadcrumb SHALL end with `ARTÍCULO: <section>]` and SHALL NOT contain a `SUBSECCIÓN` segment

#### Scenario: Fragment municipality
- **WHEN** a document's fragments are stored
- **THEN** each `ChildFragment.Municipality` and the `Municipality` of its Qdrant payload SHALL be the match key of the parent document's `Municipality` (null, and an empty payload value, when the document has none)

#### Scenario: Enrichment model fails
- **WHEN** the LLM call fails
- **THEN** the fragment SHALL still be stored, with the breadcrumb, an empty questions section and the original text, and the failure logged

### Requirement: Query Expansion
`QueryExpansionService` SHALL ask the ingestion model to translate the citizen's question into a JSON object with `query_lexica` (keywords for full-text search), `query_semantica` (a formal phrase for the embedding) and `filtro_municipio` (a municipality or null).

#### Scenario: Expanding a colloquial query
- **WHEN** a user asks "dónde pido la ayuda del alquiler si tengo 24 años"
- **THEN** the service SHALL return the three fields, accepting the JSON inside a code fence and replacing missing queries with the user's text

#### Scenario: Expansion fails
- **WHEN** the model call fails or its answer is not valid JSON
- **THEN** the service SHALL use the user's words as lexical query, the user's question as semantic query and no municipality filter

### Requirement: Hybrid Search with Reciprocal Rank Fusion
`HybridRetrievalService` SHALL combine a dense ranking and a sparse ranking of child fragments and keep the best 5.

#### Scenario: Executing a hybrid search
- **WHEN** an expanded query is retrieved
- **THEN** the dense ranking SHALL come from Qdrant (`child_fragments`, cosine) with the embedding of `query_semantica`
- **AND** the sparse ranking from PostgreSQL over `ingestion."ChildFragments"."TsvContent"` (Spanish `tsvector` filled by a trigger, GIN index), matched with `websearch_to_tsquery('spanish', query_lexica)` and ordered by `ts_rank_cd`
- **AND** both SHALL be fused with RRF (`1/(60 + rank)`, 1-based) into the top 5 fragments

#### Scenario: Vector store unavailable
- **WHEN** the Qdrant search fails
- **THEN** the retrieval SHALL log a warning and continue with the full-text ranking only

#### Scenario: Filtering by municipality
- **WHEN** the query expansion returns a municipality
- **THEN** both searches SHALL keep the fragments whose `Municipality` contains the match key of that municipality and the fragments without a municipality
- **AND** the match SHALL NOT depend on case, accents or an "Ayuntamiento de" prefix: "Ayuntamiento de SÉDELLA" SHALL match the fragments of a document of Sedella

#### Scenario: Filtering by another municipality
- **WHEN** the query expansion returns "Ronda"
- **THEN** both searches SHALL exclude the fragments of a document of Sedella and SHALL keep the fragments without a municipality

### Requirement: Parent Resolution for Generation
The system SHALL resolve the retrieved fragments to their parent documents and give the LLM the full text of each distinct parent once.

#### Scenario: Resolving parent context
- **WHEN** the top 5 fragments are retrieved
- **THEN** the system SHALL load them with their `ParentDocument`
- **AND** pass the `FullText` of each distinct parent to the generation service together with the parent's bulletin id (`DocumentId`), title (`NormTitle`), bulletin, issuing body and publication date

#### Scenario: Labelling a parent document in the prompt
- **WHEN** `ClearLanguageGenerationService` builds the prompt for parent `BOE-A-2026-1234`
- **THEN** the document SHALL be introduced by `[Documento: <title> | Boletín: BOE | Referencia: BOE-A-2026-1234 | Organismo: <issuing body> | Fecha: <yyyy-MM-dd> | URL: https://www.boe.es/buscar/doc.php?id=BOE-A-2026-1234]`, with the same public URL as the baseline pipeline (`BaselineRagPipeline.GetPublicUrl`; BOJA uses `https://www.juntadeandalucia.es/eboja.html`)
- **AND** the database id of the parent SHALL NOT be used as its label

### Requirement: Clear-Language Generation
`ClearLanguageGenerationService` SHALL stream the answer from the platform chat model (`Ai:Chat:*`) with the `ClearLanguageGeneration` prompt, which asks for a friendly "tú" tone, markdown `##` headings, lists and bold for key data, short explanations of legal terms, a final "Fuentes consultadas" section naming the articles or laws used and citing each document by its title, bulletin and reference (with its URL when it has one), and answers based only on the provided documents.

#### Scenario: Generating a structured response
- **WHEN** parent texts are provided
- **THEN** the model SHALL be prompted to answer in plain language with headings it chooses and to end with "Fuentes consultadas"

#### Scenario: Documents do not answer the question
- **WHEN** the provided documents do not contain the answer
- **THEN** the model SHALL be prompted to say clearly that it does not have that information instead of answering from its own knowledge

#### Scenario: No fragments retrieved
- **WHEN** the retrieval returns no fragments
- **THEN** the service SHALL answer the fixed message "Lo siento, no he encontrado información legal o normativa relevante en la base de datos para responder a tu consulta." without calling the model

#### Scenario: Model failure
- **WHEN** the plain-language model fails, whether the stream cannot start or fails after some text
- **THEN** the call SHALL be recorded as failed with its typed code and the stream SHALL throw `LlmStreamInterruptedException` with that code (never a generic answer text), so the chat shows the typed error, the hub keeps the partial text with the notice, and the Arena marks the side as failed
- **AND** the non-streaming generation (Arena page) SHALL rethrow the failure instead of returning an error text

#### Scenario: Recording a user's answer
- **WHEN** the hierarchical pipeline streams an answer for a user (chat, Arena side or shadow test)
- **THEN** the call SHALL be recorded through `IAiCallRecorder` in the tenant's `AiCallLogs` (platform chat model, tenant, user, pipeline, success, duration and tokens) and in the `gen_ai.*` metrics, like the baseline's answers
- **AND** auxiliary platform-model calls (query expansion, enrichment, history analysis) and the non-streaming Arena comparison SHALL only be recorded in the `gen_ai.*` metrics

## Architecture Decision Records (ADR)

### ADR: Flat Chunk Retrieval vs. Hierarchical Parent-Child Retrieval

**Decision:** We add a hierarchical Parent-Child retrieval pipeline next to the baseline flat-chunk pipeline, and compare both in the Question Arena.

**Context and Problem Statement:**
The baseline pipeline embeds documents as flat chunks of about 400 tokens (`FixedOverlapChunkingStrategy`) and searches them by dense similarity only (Qdrant `document_chunks`). For a citizen-facing legal assistant this has limits:

1. **Fragmented context:** the LLM receives isolated chunks; requirements, deadlines and exceptions of the same norm may be split across chunks that are not all retrieved.
2. **No lexical recall:** pure dense search struggles with exact terms (article numbers, identifiers such as "BOE-A-2024-1234", dates, proper nouns).
3. **No query adaptation:** citizens use colloquial language while legal texts use administrative jargon ("ayuda del alquiler" vs. "subvención de vivienda").
4. **Opaque generation:** without guidance the LLM reproduces legal phrasing verbatim.

**Considered Options:**

| Criterion | Option A: Flat Chunks (Baseline) | Option B: Parent-Child + Hybrid Search (Chosen) |
|---|---|---|
| **Indexed unit** | ~400-token chunk (`DocumentChunks`) | ~400-token child fragment enriched with breadcrumb and two synthetic questions (`ChildFragments`) |
| **Context for LLM** | The retrieved chunks | Full text of each distinct parent document (`ParentDocuments.FullText`) |
| **Search type** | Dense only (Qdrant HNSW) | Dense (Qdrant HNSW) + sparse (PostgreSQL GIN tsvector) fused with RRF |
| **Query processing** | Direct embedding of the user query | LLM query expansion: lexical terms + semantic phrase + municipality filter |
| **Ingestion cost** | Embeddings only | Embeddings + one LLM enrichment call per fragment |
| **Query cost** | One embedding | One LLM expansion call + one embedding |
| **Generation** | Tenant-configurable chat model | Platform chat model with the plain-language prompt |

**Decision Rationale:**
- **Precision over cost:** the extra embedding tokens and enrichment calls are a one-time ingestion cost, small compared to the expected quality gain.
- **Hybrid search adds recall:** RRF combines both rankings without learned weights, and the full-text ranking covers the exact terms dense search misses.
- **Parent resolution preserves coherence:** the LLM receives whole documents instead of isolated fragments.
- **Measurable:** the Question Arena collects human preferences between both pipelines, and `IngestionMetrics` records the ingestion cost of each (tokens embedded, LLM calls and tokens, duration, chunks).

**Consequences:**
- Both pipelines keep their own data: `DocumentChunks` + Qdrant `document_chunks` (baseline) and `ParentDocuments`/`ChildFragments` + Qdrant `child_fragments` (hierarchical); each document is ingested by both Workers.
- Whole-document parents can be long; very long documents increase the generation context.
- Re-ranking or cross-encoder stages can be added on top of the fused results without schema changes.
