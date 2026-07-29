package bopma

import (
	"bytes"
	"context"
	"fmt"
	"io"
	"net/http"
	"strings"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
	"github.com/ledongthuc/pdf"
	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"go.opentelemetry.io/otel"
	"golang.org/x/time/rate"
)

// Provider implementa scraper.BoletinProvider para el BOPMA.
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
	return "BOPMA"
}

func (p *Provider) FetchSummary(ctx context.Context, date time.Time) ([]string, error) {
	ctx, span := otel.Tracer("bopma-client").Start(ctx, "FetchSummary")
	defer span.End()

	// TODO: Implementar parseo del índice del BOPMA y filtrar por relevancia.
	return []string{}, nil
}

func (p *Provider) FetchDocument(ctx context.Context, id string) (*scraper.Document, []byte, error) {
	ctx, span := otel.Tracer("bopma-client").Start(ctx, "FetchDocument")
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
		return nil, nil, fmt.Errorf("status %d obteniendo PDF: %s", resp.StatusCode, id)
	}

	pdfData, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, nil, fmt.Errorf("error leyendo PDF: %w", err)
	}

	// Parse PDF content using github.com/ledongthuc/pdf
	pdfReader, err := pdf.NewReader(bytes.NewReader(pdfData), int64(len(pdfData)))
	if err != nil {
		return nil, nil, fmt.Errorf("error inicializando pdf reader: %w", err)
	}

	var textBuilder strings.Builder
	totalPage := pdfReader.NumPage()
	for pageIndex := 1; pageIndex <= totalPage; pageIndex++ {
		p := pdfReader.Page(pageIndex)
		if p.V.IsNull() {
			continue
		}
		pageText, err := p.GetPlainText(nil)
		if err != nil {
			continue
		}
		textBuilder.WriteString(pageText)
		textBuilder.WriteString("\n")
	}
	
	cleanText := strings.TrimSpace(textBuilder.String())

	// Basic document ID extraction
	docId := id
	parts := strings.Split(id, "/")
	if len(parts) > 0 {
		docId = "BOPMA-" + parts[len(parts)-1]
		docId = strings.ReplaceAll(docId, ".pdf", "")
	}

	doc := &scraper.Document{
		DocumentID: docId,
		Metadata: scraper.Metadata{
			Source:           p.Name(),
			DocumentID:       docId,
			Titulo:           "Documento BOPMA",
			Departamento:     "Diputación de Málaga",
			FechaPublicacion: time.Now().Format("2006-01-02"),
		},
		Text: cleanText,
	}

	return doc, pdfData, nil
}
