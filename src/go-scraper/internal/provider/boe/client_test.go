package boe

import (
	"context"
	"net/http"
	"net/http/httptest"
	"os"
	"strings"
	"sync/atomic"
	"testing"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/httpx"
	"github.com/asistente-ayuntamiento/go-scraper/internal/provider"
)

// newTestProvider serves the handler in place of boe.es, with a client that retries without waiting.
func newTestProvider(t *testing.T, handler http.HandlerFunc) *Provider {
	t.Helper()
	srv := httptest.NewServer(handler)
	t.Cleanup(srv.Close)
	return NewProvider(httpx.New(httpx.Options{RequestsPerSecond: 1000, Burst: 10, BaseDelay: time.Millisecond}), srv.URL)
}

func fixture(t *testing.T, name string) []byte {
	t.Helper()
	data, err := os.ReadFile("testdata/" + name)
	if err != nil {
		t.Fatal(err)
	}
	return data
}

func TestFetchSummaryKeepsOnlyDocumentsOfTheDefaultSections(t *testing.T) {
	var gotAccept, gotPath string
	p := newTestProvider(t, func(w http.ResponseWriter, r *http.Request) {
		gotAccept, gotPath = r.Header.Get("Accept"), r.URL.Path
		w.Write(fixture(t, "sumario.json"))
	})
	date := time.Date(2026, 10, 1, 0, 0, 0, 0, time.UTC)

	refs, err := p.FetchSummary(context.Background(), date, provider.Options{})
	if err != nil {
		t.Fatal(err)
	}
	var ids []string
	for _, ref := range refs {
		ids = append(ids, ref.ID)
		if !ref.PublishedOn.Equal(date) {
			t.Fatalf("ref %s published on %v", ref.ID, ref.PublishedOn)
		}
	}

	// Sections 1, 3 and 5A are fetched by default; 2A is not, and BOE-B announcements are skipped.
	want := []string{"BOE-A-2026-1001", "BOE-A-2026-1002", "BOE-A-2026-3001", "BOE-A-2026-5001"}
	if strings.Join(sorted(ids), ",") != strings.Join(want, ",") {
		t.Fatalf("ids = %v, want %v", ids, want)
	}
	if gotPath != "/datosabiertos/api/boe/sumario/20261001" || gotAccept != "application/json" {
		t.Fatalf("request path %q accept %q", gotPath, gotAccept)
	}
}

func TestFetchDocumentParsesMetadataAndStripsMarkup(t *testing.T) {
	p := newTestProvider(t, func(w http.ResponseWriter, r *http.Request) {
		w.Write(fixture(t, "BOE-A-2026-1001.xml"))
	})

	doc, raw, err := p.FetchDocument(context.Background(), provider.DocumentRef{ID: "BOE-A-2026-1001"})
	if err != nil {
		t.Fatal(err)
	}

	if doc.DocumentID != "BOE-A-2026-1001" || doc.Metadata.Source != "BOE" {
		t.Fatalf("unexpected identity: %+v", doc.Metadata)
	}
	if doc.Metadata.Titulo != "Real Decreto 100/2026, de ayudas al alquiler" || doc.Metadata.Departamento != "Ministerio de Vivienda" || doc.Metadata.FechaPublicacion != "20261001" {
		t.Fatalf("unexpected metadata: %+v", doc.Metadata)
	}
	if strings.Contains(doc.Text, "<p") || strings.Contains(doc.Text, "<b>") || !strings.Contains(doc.Text, "Las ayudas & subvenciones se concederán a jóvenes <35 años.") {
		t.Fatalf("text not cleaned: %q", doc.Text)
	}
	if string(raw.Data) != string(fixture(t, "BOE-A-2026-1001.xml")) || raw.Extension != "xml" || raw.ContentType != "application/xml" {
		t.Fatal("raw XML must be returned unchanged, as xml")
	}
}

func TestFetchDocumentWithoutMetadataIsInvalid(t *testing.T) {
	p := newTestProvider(t, func(w http.ResponseWriter, r *http.Request) {
		w.Write([]byte(`<documento><metadatos></metadatos></documento>`))
	})

	if _, _, err := p.FetchDocument(context.Background(), provider.DocumentRef{ID: "BOE-A-X"}); err == nil {
		t.Fatal("expected an error for a document without metadata")
	}
}

func TestServerErrorsAreRetriedAndNotFoundIsNot(t *testing.T) {
	var calls atomic.Int32
	p := newTestProvider(t, func(w http.ResponseWriter, r *http.Request) {
		switch {
		case strings.Contains(r.URL.RawQuery, "missing"):
			calls.Add(1)
			http.NotFound(w, r)
		case calls.Add(1) < 2:
			w.WriteHeader(http.StatusServiceUnavailable)
		default:
			w.Write(fixture(t, "BOE-A-2026-1001.xml"))
		}
	})

	if _, _, err := p.FetchDocument(context.Background(), provider.DocumentRef{ID: "BOE-A-2026-1001"}); err != nil {
		t.Fatalf("a 503 followed by 200 must succeed: %v", err)
	}

	calls.Store(0)
	if _, _, err := p.FetchDocument(context.Background(), provider.DocumentRef{ID: "missing"}); err == nil {
		t.Fatal("404 must fail")
	}
	if calls.Load() != 1 {
		t.Fatalf("404 was requested %d times, want 1 (no retry)", calls.Load())
	}
}

func TestSectionsCanBeChosenPerCall(t *testing.T) {
	p := newTestProvider(t, func(w http.ResponseWriter, r *http.Request) { w.Write(fixture(t, "sumario.json")) })

	refs, err := p.FetchSummary(context.Background(), time.Date(2026, 10, 1, 0, 0, 0, 0, time.UTC), provider.Options{Sections: []string{"2A"}})
	if err != nil {
		t.Fatal(err)
	}
	if len(refs) != 1 || refs[0].ID != "BOE-A-2026-2001" {
		t.Fatalf("refs = %+v", refs)
	}
}

func TestDayWithoutBulletinIsEmpty(t *testing.T) {
	p := newTestProvider(t, http.NotFound)

	refs, err := p.FetchSummary(context.Background(), time.Date(2026, 10, 4, 0, 0, 0, 0, time.UTC), provider.Options{})
	if err != nil || len(refs) != 0 {
		t.Fatalf("refs=%v err=%v", refs, err)
	}
}

func sorted(values []string) []string {
	out := append([]string(nil), values...)
	for i := range out {
		for j := i + 1; j < len(out); j++ {
			if out[j] < out[i] {
				out[i], out[j] = out[j], out[i]
			}
		}
	}
	return out
}
