package pipeline

import (
	"context"
	"errors"
	"fmt"
	"sync"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/messaging"
	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
	"github.com/asistente-ayuntamiento/go-scraper/internal/provider"
	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
)

// fakeProvider lists `perDay` documents per day; titles alternate "ayudas" / "nombramiento".
type fakeProvider struct {
	name    string
	perDay  int
	block   chan struct{} // when set, FetchDocument waits on it
	failIDs map[string]bool

	mu      sync.Mutex
	days    []string
	options []provider.Options
}

func (p *fakeProvider) Name() string { return p.name }

func (p *fakeProvider) FetchSummary(ctx context.Context, date time.Time, opts provider.Options) ([]provider.DocumentRef, error) {
	p.mu.Lock()
	p.days = append(p.days, date.Format(time.DateOnly))
	p.options = append(p.options, opts)
	p.mu.Unlock()
	var refs []provider.DocumentRef
	for i := 1; i <= p.perDay; i++ {
		refs = append(refs, provider.DocumentRef{ID: fmt.Sprintf("%s-%s-%d", p.name, date.Format("20060102"), i), PublishedOn: date})
	}
	return refs, nil
}

func (p *fakeProvider) FetchDocument(ctx context.Context, ref provider.DocumentRef) (*scraper.Document, provider.Raw, error) {
	if p.block != nil {
		select {
		case <-p.block:
		case <-ctx.Done():
			return nil, provider.Raw{}, ctx.Err()
		}
	}
	if p.failIDs[ref.ID] {
		return nil, provider.Raw{}, errors.New("download failed")
	}
	title := "Nombramiento de personal"
	if ref.ID[len(ref.ID)-1]%2 == 1 {
		title = "Bases de ayudas al alquiler"
	}
	return &scraper.Document{
		DocumentID: ref.ID,
		Metadata:   scraper.Metadata{Source: p.name, DocumentID: ref.ID, Titulo: title},
	}, provider.Raw{Data: []byte("<xml/>"), Extension: "xml", ContentType: "application/xml"}, nil
}

type fakeStorage struct {
	mu        sync.Mutex
	documents []string
	raws      []string
	failSave  bool
}

func (s *fakeStorage) SaveDocument(ctx context.Context, doc *scraper.Document) error {
	if s.failSave {
		return errors.New("bucket unavailable")
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	s.documents = append(s.documents, doc.DocumentID)
	return nil
}

func (s *fakeStorage) SaveRaw(ctx context.Context, source, id string, raw provider.Raw) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.raws = append(s.raws, source+"/"+id+"."+raw.Extension)
	return nil
}

func (s *fakeStorage) Close() error { return nil }

type fakePublisher struct {
	mu       sync.Mutex
	messages []messaging.DocumentMessage
}

func (p *fakePublisher) PublishDocument(ctx context.Context, msg messaging.DocumentMessage) error {
	p.mu.Lock()
	defer p.mu.Unlock()
	p.messages = append(p.messages, msg)
	return nil
}

type fakeFilters struct {
	rules []*pb.FilterRule
	err   error
}

func (f fakeFilters) GetFilters(ctx context.Context) ([]*pb.FilterRule, error) { return f.rules, f.err }
