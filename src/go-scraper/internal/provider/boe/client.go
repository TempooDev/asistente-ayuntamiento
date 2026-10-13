// Package boe reads the Boletín Oficial del Estado through its open data API.
package boe

import (
	"context"
	"encoding/json"
	"encoding/xml"
	"fmt"
	"slices"
	"strings"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/httpx"
	"github.com/asistente-ayuntamiento/go-scraper/internal/provider"
	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
)

const (
	Name           = "BOE"
	DefaultBaseURL = "https://www.boe.es"
)

// DefaultSections: general provisions, other provisions and procurement announcements.
var DefaultSections = []string{"1", "2B", "3", "5A"}

var tracer = otel.Tracer("go-scraper/boe")

type Provider struct {
	client  *httpx.Client
	baseURL string
}

// NewProvider uses baseURL for every request ("" means DefaultBaseURL).
func NewProvider(client *httpx.Client, baseURL string) *Provider {
	if baseURL == "" {
		baseURL = DefaultBaseURL
	}
	return &Provider{client: client, baseURL: strings.TrimRight(baseURL, "/")}
}

// NewHTTPClient is the rate-limited client the BOE tolerates (2 requests per second).
func NewHTTPClient() *httpx.Client {
	return httpx.New(httpx.Options{RequestsPerSecond: 2, Burst: 2})
}

func (p *Provider) Name() string { return Name }

// FetchSummary reads the day's summary and keeps the BOE-A documents of the selected sections.
func (p *Provider) FetchSummary(ctx context.Context, date time.Time, opts provider.Options) ([]provider.DocumentRef, error) {
	ctx, span := tracer.Start(ctx, "boe.FetchSummary")
	defer span.End()

	sections := opts.Sections
	if len(sections) == 0 {
		sections = DefaultSections
	}
	span.SetAttributes(attribute.String("bulletin.date", date.Format(time.DateOnly)), attribute.StringSlice("boe.sections", sections))

	body, err := p.client.Get(ctx, fmt.Sprintf("%s/datosabiertos/api/boe/sumario/%s", p.baseURL, date.Format("20060102")), map[string]string{"Accept": "application/json"})
	if err != nil {
		if httpx.IsNotFound(err) {
			return nil, nil // No bulletin that day (Sundays, holidays).
		}
		return nil, fmt.Errorf("BOE summary %s: %w", date.Format(time.DateOnly), err)
	}

	var summary any
	if err := json.Unmarshal(body, &summary); err != nil {
		return nil, fmt.Errorf("BOE summary %s: invalid JSON: %w", date.Format(time.DateOnly), err)
	}

	var refs []provider.DocumentRef
	collectDocuments(summary, false, sections, func(id string) {
		refs = append(refs, provider.DocumentRef{ID: id, PublishedOn: date})
	})
	return refs, nil
}

// collectDocuments walks the summary tree. A node with "codigo" and "departamento" is a section; documents
// ("identificador" BOE-A-...) below an allowed section are reported. The API returns either arrays or single
// objects for departments, headings and items.
func collectDocuments(node any, inAllowedSection bool, sections []string, found func(string)) {
	switch n := node.(type) {
	case map[string]any:
		if code, ok := n["codigo"].(string); ok && n["departamento"] != nil {
			inAllowedSection = slices.Contains(sections, code)
		}
		if id, ok := n["identificador"].(string); ok && inAllowedSection && strings.HasPrefix(id, "BOE-A-") {
			found(id)
		}
		for _, child := range n {
			collectDocuments(child, inAllowedSection, sections, found)
		}
	case []any:
		for _, child := range n {
			collectDocuments(child, inAllowedSection, sections, found)
		}
	}
}

type documentXML struct {
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

// FetchDocument downloads the document XML and extracts its metadata and plain text.
func (p *Provider) FetchDocument(ctx context.Context, ref provider.DocumentRef) (*scraper.Document, provider.Raw, error) {
	ctx, span := tracer.Start(ctx, "boe.FetchDocument")
	defer span.End()
	span.SetAttributes(attribute.String("document.id", ref.ID))

	data, err := p.client.Get(ctx, fmt.Sprintf("%s/diario_boe/xml.php?id=%s", p.baseURL, ref.ID), nil)
	if err != nil {
		return nil, provider.Raw{}, fmt.Errorf("BOE document %s: %w", ref.ID, err)
	}
	raw := provider.Raw{Data: data, Extension: "xml", ContentType: "application/xml"}

	var parsed documentXML
	if err := xml.Unmarshal(data, &parsed); err != nil {
		return nil, raw, fmt.Errorf("BOE document %s: invalid XML: %w", ref.ID, err)
	}
	if parsed.Metadatos.Identificador == "" {
		return nil, raw, fmt.Errorf("BOE document %s: no metadata", ref.ID)
	}

	id := parsed.Metadatos.Identificador
	return &scraper.Document{
		DocumentID: id,
		Metadata: scraper.Metadata{
			Source:           Name,
			DocumentID:       id,
			Titulo:           parsed.Metadatos.Titulo,
			Departamento:     parsed.Metadatos.Departamento,
			FechaPublicacion: parsed.Metadatos.FechaPublicacion,
		},
		Text: scraper.StripHTMLTags(strings.TrimSpace(parsed.Texto.InnerXML)),
	}, raw, nil
}
