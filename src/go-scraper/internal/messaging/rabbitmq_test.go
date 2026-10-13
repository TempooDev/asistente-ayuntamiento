package messaging

import (
	"context"
	"encoding/json"
	"testing"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/propagation"
	"go.opentelemetry.io/otel/trace"
)

func TestDocumentMessageCarriesThePublishedPipelines(t *testing.T) {
	msg := DocumentMessage{Source: "BOJA", DocumentID: "BOJA-2026-198-1", BlobPath: "json/BOJA/BOJA-2026-198-1.json", Pipelines: PublishedPipelines}

	body, err := json.Marshal(msg)
	if err != nil {
		t.Fatal(err)
	}

	want := `{"source":"BOJA","document_id":"BOJA-2026-198-1","blob_path":"json/BOJA/BOJA-2026-198-1.json","pipelines":["BASELINE","HIERARCHICAL"]}`
	if string(body) != want {
		t.Fatalf("body = %s, want %s", body, want)
	}
}

func TestTraceHeadersCarryTheW3CContext(t *testing.T) {
	previous := otel.GetTextMapPropagator()
	otel.SetTextMapPropagator(propagation.TraceContext{})
	t.Cleanup(func() { otel.SetTextMapPropagator(previous) })

	traceID, _ := trace.TraceIDFromHex("4bf92f3577b34da6a3ce929d0e0e4736")
	spanID, _ := trace.SpanIDFromHex("00f067aa0ba902b7")
	ctx := trace.ContextWithSpanContext(context.Background(), trace.NewSpanContext(trace.SpanContextConfig{
		TraceID:    traceID,
		SpanID:     spanID,
		TraceFlags: trace.FlagsSampled,
	}))

	headers := TraceHeaders(ctx)

	if got, want := headers["traceparent"], "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"; got != want {
		t.Fatalf("traceparent = %v, want %v", got, want)
	}
}

func TestTraceHeadersAreEmptyWithoutASpan(t *testing.T) {
	if headers := TraceHeaders(context.Background()); len(headers) != 0 {
		t.Fatalf("headers = %v, want none", headers)
	}
}
