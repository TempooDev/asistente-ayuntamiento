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

// Document representa la salida final estructurada del procesamiento de una publicación.
// Este documento será procesado posteriormente en .NET (Semantic Kernel) para el chunking y embeddings.
type Document struct {
	DocumentID string   `json:"document_id"`
	Metadata   Metadata `json:"metadata"`
	Text       string   `json:"text"`
}
