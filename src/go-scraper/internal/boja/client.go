package boja

import (
	"context"
	"encoding/xml"
	"fmt"
	"io"
	"net/http"
	"regexp"
	"strings"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"go.opentelemetry.io/otel"
	"golang.org/x/time/rate"
)

// Provider implementa scraper.BoletinProvider para el BOJA.
type Provider struct {
	httpClient  *http.Client
	rateLimiter *rate.Limiter
	feeds       []string
}

func (p *Provider) UpdateFeeds(feeds []string) {
	if len(feeds) > 0 {
		p.feeds = feeds
	}
}

func NewProvider(customFeeds ...string) *Provider {
	feeds := customFeeds
	if len(feeds) == 0 {
		feeds = []string{
			"https://www.juntadeandalucia.es/boja/distribucion/s51.xml",
			"https://www.juntadeandalucia.es/boja/distribucion/s52.xml",
			"https://www.juntadeandalucia.es/boja/distribucion/s53.xml",
			"https://www.juntadeandalucia.es/boja/distribucion/s54.xml",
			"https://www.juntadeandalucia.es/boja/distribucion/s55.xml",
		}
	}

	limiter := rate.NewLimiter(rate.Every(1*time.Second), 1)
	return &Provider{
		httpClient: &http.Client{
			Transport: otelhttp.NewTransport(http.DefaultTransport),
			Timeout:   30 * time.Second,
		},
		rateLimiter: limiter,
		feeds:       feeds,
	}
}

func (p *Provider) Name() string {
	return "BOJA"
}

func (p *Provider) FetchSummary(ctx context.Context, date time.Time) ([]string, error) {
	ctx, span := otel.Tracer("boja-client").Start(ctx, "FetchSummary")
	defer span.End()

	// Si es el día de hoy, el XML es más rápido y estable.
	// Nota: El XML del BOJA no contiene fecha, siempre tiene el último.
	now := time.Now()
	if date.Year() == now.Year() && date.YearDay() == now.YearDay() {
		return p.fetchLatestFromXML(ctx)
	}

	// Para histórico, buscamos en el buscador HTML
	return p.fetchFromHTMLSearch(ctx, date)
}

func (p *Provider) fetchLatestFromXML(ctx context.Context) ([]string, error) {

	var ids []string
	for _, feed := range p.feeds {
		req, err := http.NewRequestWithContext(ctx, http.MethodGet, feed, nil)
		if err != nil {
			continue
		}

		resp, err := p.httpClient.Do(req)
		if err != nil {
			continue
		}
		defer resp.Body.Close()

		if resp.StatusCode != http.StatusOK {
			continue
		}

		var atom struct {
			Entries []struct {
				Link struct {
					Href string `xml:"href,attr"`
				} `xml:"link"`
			} `xml:"entry"`
		}

		if err := xml.NewDecoder(resp.Body).Decode(&atom); err == nil {
			for _, entry := range atom.Entries {
				if entry.Link.Href != "" {
					ids = append(ids, entry.Link.Href)
				}
			}
		}
	}

	return ids, nil
}

func (p *Provider) FetchDocument(ctx context.Context, id string) (*scraper.Document, []byte, error) {
	ctx, span := otel.Tracer("boja-client").Start(ctx, "FetchDocument")
	defer span.End()

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, id, nil)
	if err != nil {
		return nil, nil, err
	}

	resp, err := p.httpClient.Do(req)
	if err != nil {
		return nil, nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, nil, fmt.Errorf("status %d", resp.StatusCode)
	}

	htmlContent, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, nil, err
	}

	cleanText := scraper.StripHTMLTags(string(htmlContent))
	
	// Create a stable document ID from the URL
	// Example: https://www.juntadeandalucia.es/boja/2026/123/1
	docId := id
	parts := strings.Split(strings.TrimRight(id, "/"), "/")
	if len(parts) >= 3 {
		// e.g. boja/2026/123/1 -> BOJA-2026-123-1
		docId = "BOJA-" + strings.Join(parts[len(parts)-3:], "-")
		docId = strings.ReplaceAll(docId, ".html", "")
	}

	// Extract title from HTML
	titulo := "Documento BOJA"
	titleStart := strings.Index(string(htmlContent), "<title>")
	titleEnd := strings.Index(string(htmlContent), "</title>")
	if titleStart != -1 && titleEnd != -1 && titleEnd > titleStart {
		titulo = string(htmlContent)[titleStart+7 : titleEnd]
		titulo = strings.TrimSpace(strings.ReplaceAll(titulo, "\n", " "))
	}

	doc := &scraper.Document{
		DocumentID: docId,
		Metadata: scraper.Metadata{
			Source:           p.Name(),
			DocumentID:       docId,
			Titulo:           titulo,
			Departamento:     "Junta de Andalucía",
			FechaPublicacion: time.Now().Format("2006-01-02"),
		},
		Text: cleanText,
	}

	return doc, htmlContent, nil
}

func (p *Provider) fetchFromHTMLSearch(ctx context.Context, date time.Time) ([]string, error) {
	// Buscador avanzado BOJA: eboja/buscador/search.do
	// Puede sufrir timeouts (Error 500) intermitentes en la web de la Junta.
	dateStr := date.Format("02/01/2006")
	searchURL := fmt.Sprintf("https://www.juntadeandalucia.es/eboja/buscador/search.do?startDate=%s&endDate=%s&eboja=on&q=&summary=&type=&section=&organisation=&ordenacion=&sentido_ordenacion=", dateStr, dateStr)

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, searchURL, nil)
	if err != nil {
		return nil, fmt.Errorf("error creando request HTML BOJA: %w", err)
	}
	
	// Timeout especial más largo porque el buscador en Solr suele tardar
	client := &http.Client{Timeout: 60 * time.Second}
	resp, err := client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("error llamando buscador HTML BOJA: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("buscador HTML BOJA devolvió status: %d", resp.StatusCode)
	}

	bodyBytes, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}
	htmlContent := string(bodyBytes)

	// Extraer enlaces con expresiones regulares simples a "<a href="http://www.juntadeandalucia.es/boja/..."
	// Esto es un scraper muy básico para el HTML devuelto por la búsqueda.
	var ids []string
	
	// Buscamos algo tipo: href="http://www.juntadeandalucia.es/boja/2026/167/1"
	re := regexp.MustCompile(`href="(https?://www\.juntadeandalucia\.es/boja/\d{4}/\d+/\d+(?:\.html)?)"`)
	matches := re.FindAllStringSubmatch(htmlContent, -1)
	
	seen := make(map[string]bool)
	for _, m := range matches {
		link := m[1]
		if !seen[link] {
			seen[link] = true
			ids = append(ids, link)
		}
	}
	
	return ids, nil
}
