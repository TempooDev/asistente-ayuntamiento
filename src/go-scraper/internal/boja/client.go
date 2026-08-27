package boja

import (
	"context"
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

// Provider implementa scraper.BoletinProvider para el BOJA.
type Provider struct {
	httpClient  *http.Client
	rateLimiter *rate.Limiter
}

func NewProvider() *Provider {
	limiter := rate.NewLimiter(rate.Every(1*time.Second), 1)
	return &Provider{
		httpClient: &http.Client{
			Transport: otelhttp.NewTransport(http.DefaultTransport),
			Timeout:   30 * time.Second,
		},
		rateLimiter: limiter,
	}
}

func (p *Provider) Name() string {
	return "BOJA"
}

func (p *Provider) FetchSummary(ctx context.Context, date time.Time) ([]string, error) {
	ctx, span := otel.Tracer("boja-client").Start(ctx, "FetchSummary")
	defer span.End()

	// BOJA feeds per section:
	// s51.xml = 1. Disposiciones generales
	// s52.xml = 2. Autoridades y personal
	// s53.xml = 3. Otras disposiciones
	feeds := []string{
		"https://www.juntadeandalucia.es/boja/distribucion/s51.xml",
		"https://www.juntadeandalucia.es/boja/distribucion/s52.xml",
		"https://www.juntadeandalucia.es/boja/distribucion/s53.xml",
	}

	var ids []string
	for _, feed := range feeds {
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
