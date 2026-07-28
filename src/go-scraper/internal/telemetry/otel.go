package telemetry

import (
	"context"
	"fmt"
	"os"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracegrpc"
	"go.opentelemetry.io/otel/propagation"
	"go.opentelemetry.io/otel/sdk/resource"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	semconv "go.opentelemetry.io/otel/semconv/v1.24.0"
)

// InitProvider inicializa el proveedor global de traces usando las variables
// de entorno inyectadas por Aspire (ej. OTEL_EXPORTER_OTLP_ENDPOINT).
func InitProvider(ctx context.Context) (func(context.Context) error, error) {
	// Aspire inyecta automáticamente OTEL_EXPORTER_OTLP_ENDPOINT si está habilitado.
	endpoint := os.Getenv("OTEL_EXPORTER_OTLP_ENDPOINT")
	if endpoint == "" {
		// Si no hay endpoint (ej. ejecutando standalone), mockeamos el shutdown y no exportamos.
		return func(context.Context) error { return nil }, nil
	}

	// El SDK de Go lee automáticamente la variable OTEL_EXPORTER_OTLP_ENDPOINT
	exporter, err := otlptracegrpc.New(ctx)
	if err != nil {
		return nil, fmt.Errorf("error creando exportador otlp: %w", err)
	}

	res, err := resource.Merge(
		resource.Default(),
		resource.NewWithAttributes(
			"", // Ignoramos el SchemaURL para evitar conflictos de versiones entre dependencias
			semconv.ServiceName("go-scraper"),
		),
	)
	if err != nil {
		return nil, fmt.Errorf("error creando resource otel: %w", err)
	}

	tp := sdktrace.NewTracerProvider(
		sdktrace.WithBatcher(exporter),
		sdktrace.WithResource(res),
	)

	otel.SetTracerProvider(tp)
	// Propagadores estándar de contexto (útil si llamáramos a otras APIs traceadas)
	otel.SetTextMapPropagator(propagation.NewCompositeTextMapPropagator(propagation.TraceContext{}, propagation.Baggage{}))

	return tp.Shutdown, nil
}
