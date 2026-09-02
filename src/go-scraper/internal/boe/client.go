package boe

import (
	"context"
	"encoding/json"
	"encoding/xml"
	"fmt"
	"io"
	"net/http"
	"strings"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"go.opentelemetry.io/otel"
	"golang.org/x/time/rate"
)

const (
	summaryURLTemplate  = "https://www.boe.es/datosabiertos/api/boe/sumario/%s"
	documentURLTemplate = "https://www.boe.es/diario_boe/xml.php?id=%s"
	maxRetries          = 3
)

// Provider implementa scraper.BoletinProvider para el BOE.
type Provider struct {
	httpClient  *http.Client
	rateLimiter *rate.Limiter
	sections    []string
}

// NewProvider crea una nueva instancia del proveedor BOE con Rate Limiting.
func NewProvider() *Provider {
	// BOE Rate Limiting: 2 peticiones por segundo máximo.
	limiter := rate.NewLimiter(rate.Every(500*time.Millisecond), 2)
	return &Provider{
		httpClient: &http.Client{
			Transport: otelhttp.NewTransport(http.DefaultTransport),
			Timeout:   30 * time.Second,
		},
		rateLimiter: limiter,
		sections:    []string{"1", "2B", "3", "5A"}, // Default sections
	}
}

func (p *Provider) SetSections(sections []string) {
	p.sections = sections
}

func (p *Provider) Name() string {
	return "BOE"
}

// FetchSummary descarga el índice JSON del BOE (Open Data API) y extrae los IDs de los documentos.
func (p *Provider) FetchSummary(ctx context.Context, date time.Time) ([]string, error) {
	ctx, span := otel.Tracer("boe-client").Start(ctx, "FetchSummary")
	defer span.End()

	dateStr := date.Format("20060102")
	url := fmt.Sprintf(summaryURLTemplate, dateStr)

	// Inyectamos el Accept: application/json en la petición
	body, err := p.doRequest(ctx, url, map[string]string{"Accept": "application/json"})
	if err != nil {
		return nil, fmt.Errorf("error obteniendo sumario BOE: %w", err)
	}
	defer body.Close()

	var data interface{}
	if err := json.NewDecoder(body).Decode(&data); err != nil {
		return nil, fmt.Errorf("error parseando sumario JSON: %w", err)
	}

	var ids []string
	var processNode func(v interface{}, active bool)
	processNode = func(v interface{}, active bool) {
		switch node := v.(type) {
		case map[string]interface{}:
			// Si el nodo es una sección, revisamos su código
			if cod, ok := node["codigo"].(string); ok && node["departamento"] != nil {
				active = false
				for _, allowed := range p.sections {
					if cod == allowed {
						active = true
						break
					}
				}
			}

			if active {
				if idVal, ok := node["identificador"].(string); ok {
					// Extraemos el identificador solo si estamos en una sección activa y es un documento (BOE-A-...)
					if strings.HasPrefix(idVal, "BOE-A-") {
						ids = append(ids, idVal)
					}
				}
			}

			// Continuar iterando por los hijos (departamentos, epígrafes, items)
			for _, val := range node {
				processNode(val, active)
			}
		case []interface{}:
			for _, item := range node {
				processNode(item, active)
			}
		}
	}

	processNode(data, false)
	return ids, nil
}

// FetchDocument descarga y estructura un documento BOE XML individual por su ID.
func (p *Provider) FetchDocument(ctx context.Context, id string) (*scraper.Document, []byte, error) {
	ctx, span := otel.Tracer("boe-client").Start(ctx, "FetchDocument")
	defer span.End()

	url := fmt.Sprintf(documentURLTemplate, id)

	body, err := p.doRequest(ctx, url)
	if err != nil {
		return nil, nil, fmt.Errorf("error obteniendo documento BOE %s: %w", id, err)
	}
	defer body.Close()
	
	rawXML, err := io.ReadAll(body)
	if err != nil {
		return nil, nil, fmt.Errorf("error leyendo raw XML de %s: %w", id, err)
	}

	// Parseo estructurado del XML del BOE.
	var docXML struct {
		Metadatos struct {
			Identificador    string `xml:"identificador"`
			Titulo           string `xml:"titulo"`
			Departamento     string `xml:"departamento"`
			FechaPublicacion string `xml:"fecha_publicacion"`
		} `xml:"metadatos"`
		Texto struct {
			InnerXML string `xml:",innerxml"`
		} `xml:"texto"`
	}

	if err := xml.Unmarshal(rawXML, &docXML); err != nil {
		return nil, rawXML, fmt.Errorf("error decodificando XML documento %s: %w", id, err)
	}

	// Si falta el identificador, asumimos que el documento no es válido o es un XML vacío.
	if docXML.Metadatos.Identificador == "" {
		return nil, rawXML, fmt.Errorf("documento %s no válido (sin metadatos)", id)
	}

	// Limpiar el HTML/XML interno para quedarse solo con el texto.
	cleanText := strings.TrimSpace(docXML.Texto.InnerXML)
	// Un reemplazo básico de tags HTML para que el motor vectorial trabaje mejor
	cleanText = scraper.StripHTMLTags(cleanText)

	doc := &scraper.Document{
		DocumentID: docXML.Metadatos.Identificador,
		Metadata: scraper.Metadata{
			Source:           p.Name(),
			DocumentID:       docXML.Metadatos.Identificador,
			Titulo:           docXML.Metadatos.Titulo,
			Departamento:     docXML.Metadatos.Departamento,
			FechaPublicacion: docXML.Metadatos.FechaPublicacion,
		},
		Text: cleanText,
	}

	return doc, rawXML, nil
}

// doRequest realiza la petición HTTP con Rate Limiting y Exponential Backoff.
func (p *Provider) doRequest(ctx context.Context, url string, headers ...map[string]string) (io.ReadCloser, error) {
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

		// Apply optional headers
		if len(headers) > 0 {
			for k, v := range headers[0] {
				req.Header.Set(k, v)
			}
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
