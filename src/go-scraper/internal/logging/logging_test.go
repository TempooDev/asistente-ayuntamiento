package logging

import (
	"bytes"
	"context"
	"encoding/json"
	"testing"

	"go.opentelemetry.io/otel/trace"
)

func TestRecordsCarryTheTraceOfTheContext(t *testing.T) {
	var out bytes.Buffer
	logger := New(&out, "info")
	traceID, _ := trace.TraceIDFromHex("0af7651916cd43dd8448eb211c80319c")
	spanID, _ := trace.SpanIDFromHex("b7ad6b7169203331")
	ctx := trace.ContextWithSpanContext(context.Background(), trace.NewSpanContext(trace.SpanContextConfig{TraceID: traceID, SpanID: spanID, TraceFlags: trace.FlagsSampled}))

	logger.InfoContext(ctx, "document saved", "document_id", "BOE-A-1")

	var record map[string]any
	if err := json.Unmarshal(out.Bytes(), &record); err != nil {
		t.Fatal(err)
	}
	if record["trace_id"] != traceID.String() || record["span_id"] != spanID.String() || record["service"] != "go-scraper" || record["document_id"] != "BOE-A-1" {
		t.Fatalf("record = %v", record)
	}
}

func TestDebugIsHiddenAtInfo(t *testing.T) {
	var out bytes.Buffer
	New(&out, "info").Debug("noise")
	if out.Len() != 0 {
		t.Fatalf("unexpected output %q", out.String())
	}
}
