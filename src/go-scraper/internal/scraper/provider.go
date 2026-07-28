package scraper

import (
	"context"
	"time"
)

// BoletinProvider define el contrato principal que todo scraper de un boletín oficial
// (BOE, BOJA, BOPMA) debe cumplir. Esto permite aislar la lógica específica de cada
// plataforma y reutilizar el motor central de orquestación y almacenamiento.
type BoletinProvider interface {
	// Name devuelve el nombre del boletín, utilizado para logs y trazabilidad (ej. "BOE", "BOJA").
	Name() string

	// FetchSummary obtiene la lista de identificadores únicos (IDs) de los documentos
	// publicados en la fecha proporcionada.
	FetchSummary(ctx context.Context, date time.Time) ([]string, error)

	// FetchDocument obtiene los detalles y el texto completo de un documento por su ID.
	// Retorna también el XML original en bytes.
	FetchDocument(ctx context.Context, id string) (*Document, []byte, error)
}
