package storage

import "testing"

func TestBlobPathsMatchTheIngestionContract(t *testing.T) {
	// Read by the .NET ingestion: DocumentIngestionService (json) and BoeIngestionService (raw-xml).
	if got := DocumentPath("BOE", "BOE-A-2026-1"); got != "json/BOE/BOE-A-2026-1.json" {
		t.Fatalf("document path %q", got)
	}
	if got := RawPath("BOE", "BOE-A-2026-1", "xml"); got != "raw-xml/BOE/BOE-A-2026-1.xml" {
		t.Fatalf("raw path %q", got)
	}
	if got := RawPath("BOJA", "BOJA-2026-1-1", "html"); got != "raw-html/BOJA/BOJA-2026-1-1.html" {
		t.Fatalf("raw path %q", got)
	}
}
