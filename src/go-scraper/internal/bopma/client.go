package bopma

import (
	"context"
	"fmt"
	"net/http"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
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

	// TODO: Descargar PDF usando el id y extraer el texto usando github.com/ledongthuc/pdf
	return nil, nil, fmt.Errorf("no implementado todavía")
}
