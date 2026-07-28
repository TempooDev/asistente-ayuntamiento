package scraper

// Metadata representa los metadatos comunes extraídos de cualquier boletín oficial.
// Estos campos se utilizan para proporcionar contexto al LLM y permitir filtrado en la base de datos vectorial.
type Metadata struct {
	Source           string `json:"source"`
	DocumentID       string `json:"document_id"`
	Titulo           string `json:"titulo"`
	Departamento     string `json:"departamento"`
	FechaPublicacion string `json:"fecha_publicacion"`
}

// Chunk representa un fragmento de texto segmentado (ej. un artículo o párrafo lógico)
// con todo su contexto (metadatos) inyectado para evitar pérdida de semántica.
type Chunk struct {
	ChunkID          string   `json:"chunk_id"`
	ChunkIndex       int      `json:"chunk_index"`
	MetadataInjected Metadata `json:"metadata_injected"`
	Text             string   `json:"text"`
}

// Document representa la salida final estructurada del procesamiento de una publicación.
// Este es el documento JSON que se almacenará finalmente en el Blob Storage.
type Document struct {
	DocumentID string   `json:"document_id"`
	Metadata   Metadata `json:"metadata"`
	Chunks     []Chunk  `json:"chunks"`
}
