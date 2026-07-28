package boe

import (
	"context"
	"encoding/xml"
	"fmt"
	"io"
	"net/http"
	"strings"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
	"golang.org/x/time/rate"
)

const (
	summaryURLTemplate  = "https://www.boe.es/diario_boe/xml.php?id=BOE-S-%s"
	documentURLTemplate = "https://www.boe.es/diario_boe/xml.php?id=%s"
	maxRetries          = 3
)

// Provider implementa scraper.BoletinProvider para el BOE.
type Provider struct {
	httpClient  *http.Client
	rateLimiter *rate.Limiter
}

// NewProvider crea una nueva instancia del proveedor BOE con Rate Limiting.
func NewProvider() *Provider {
	// BOE Rate Limiting: 2 peticiones por segundo máximo.
	limiter := rate.NewLimiter(rate.Every(500*time.Millisecond), 2)
	return &Provider{
		httpClient: &http.Client{
			Timeout: 30 * time.Second,
		},
		rateLimiter: limiter,
	}
}

func (p *Provider) Name() string {
	return "BOE"
}

// FetchSummary descarga el índice XML del BOE y extrae los IDs de los documentos.
func (p *Provider) FetchSummary(ctx context.Context, date time.Time) ([]string, error) {
	dateStr := date.Format("20060102")
	url := fmt.Sprintf(summaryURLTemplate, dateStr)

	body, err := p.doRequest(ctx, url)
	if err != nil {
		return nil, fmt.Errorf("error obteniendo sumario BOE: %w", err)
	}
	defer body.Close()

	// Usamos un decodificador flexible (token-based) para buscar todas las etiquetas <item id="...">
	// sin importar en qué nivel de la jerarquía se encuentren.
	decoder := xml.NewDecoder(body)
	var ids []string
	for {
		t, err := decoder.Token()
		if err == io.EOF {
			break
		}
		if err != nil {
			return nil, fmt.Errorf("error parseando sumario XML: %w", err)
		}
		switch se := t.(type) {
		case xml.StartElement:
			if se.Name.Local == "item" {
				for _, attr := range se.Attr {
					if attr.Name.Local == "id" {
						ids = append(ids, attr.Value)
					}
				}
			}
		}
	}

	return ids, nil
}

// FetchDocument descarga y estructura un documento BOE XML individual por su ID.
func (p *Provider) FetchDocument(ctx context.Context, id string) (*scraper.Document, error) {
	url := fmt.Sprintf(documentURLTemplate, id)

	body, err := p.doRequest(ctx, url)
	if err != nil {
		return nil, fmt.Errorf("error obteniendo documento BOE %s: %w", id, err)
	}
	defer body.Close()

	// Parseo estructurado del XML del BOE.
	var docXML struct {
		Metadatos struct {
			Identificador    string `xml:"identificador"`
			Titulo           string `xml:"titulo"`
			Departamento     string `xml:"departamento"`
			FechaPublicacion string `xml:"fecha_publicacion"`
		} `xml:"metadatos"`
		Texto string `xml:"texto"`
	}

	decoder := xml.NewDecoder(body)
	if err := decoder.Decode(&docXML); err != nil {
		return nil, fmt.Errorf("error decodificando XML documento %s: %w", id, err)
	}

	// Si falta el identificador, asumimos que el documento no es válido o es un XML vacío.
	if docXML.Metadatos.Identificador == "" {
		return nil, fmt.Errorf("documento %s no válido (sin metadatos)", id)
	}

	doc := &scraper.Document{
		DocumentID: docXML.Metadatos.Identificador,
		Metadata: scraper.Metadata{
			Source:           p.Name(),
			DocumentID:       docXML.Metadatos.Identificador,
			Titulo:           docXML.Metadatos.Titulo,
			Departamento:     docXML.Metadatos.Departamento,
			FechaPublicacion: docXML.Metadatos.FechaPublicacion,
		},
	}

	// TODO (Tarea 5): Llamar al motor de Chunking aquí.
	// Por ahora simulamos un chunking devolviendo todo el texto en el primer chunk.
	doc.Chunks = []scraper.Chunk{
		{
			ChunkID:          fmt.Sprintf("%s_chunk_1", doc.DocumentID),
			ChunkIndex:       1,
			MetadataInjected: doc.Metadata,
			Text:             strings.TrimSpace(docXML.Texto),
		},
	}

	return doc, nil
}

// doRequest realiza la petición HTTP con Rate Limiting y Exponential Backoff.
func (p *Provider) doRequest(ctx context.Context, url string) (io.ReadCloser, error) {
	var lastErr error

	for attempt := 1; attempt <= maxRetries; attempt++ {
		// Esperar si el Rate Limiter lo requiere.
		if err := p.rateLimiter.Wait(ctx); err != nil {
			return nil, fmt.Errorf("rate limiter error: %w", err)
		}

		req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
		if err != nil {
			return nil, err
		}

		resp, err := p.httpClient.Do(req)
		if err != nil {
			lastErr = err
			time.Sleep(time.Duration(attempt) * time.Second) // Exponential backoff simplificado
			continue
		}

		if resp.StatusCode != http.StatusOK {
			resp.Body.Close()
			lastErr = fmt.Errorf("http status %d", resp.StatusCode)
			// Reintentamos solo en errores de servidor (5xx) o Too Many Requests (429)
			if resp.StatusCode >= 500 || resp.StatusCode == http.StatusTooManyRequests {
				time.Sleep(time.Duration(attempt) * time.Second)
				continue
			}
			return nil, lastErr // Fallo de cliente (ej. 404), no reintentar
		}

		return resp.Body, nil
	}

	return nil, fmt.Errorf("max retries exceeded (%d), last error: %w", maxRetries, lastErr)
}
