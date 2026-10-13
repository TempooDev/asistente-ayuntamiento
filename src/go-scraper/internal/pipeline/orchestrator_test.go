package pipeline

import (
	"context"
	"errors"
	"io"
	"log/slog"
	"slices"
	"testing"
	"time"

	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
	"github.com/asistente-ayuntamiento/go-scraper/internal/provider"
)

var today = time.Date(2026, 10, 13, 9, 0, 0, 0, time.UTC)

type fixture struct {
	boe, boja *fakeProvider
	storage   *fakeStorage
	publisher *fakePublisher
	filters   fakeFilters
}

func newFixture() *fixture {
	return &fixture{
		boe:       &fakeProvider{name: "BOE", perDay: 4},
		boja:      &fakeProvider{name: "BOJA", perDay: 2},
		storage:   &fakeStorage{},
		publisher: &fakePublisher{},
	}
}

func (f *fixture) orchestrator() *Orchestrator {
	return New(Config{
		Providers:    []provider.Provider{f.boe, f.boja},
		Storage:      f.storage,
		Publisher:    f.publisher,
		Filters:      f.filters,
		MaxRangeDays: 7,
		Logger:       slog.New(slog.NewTextHandler(io.Discard, nil)),
		Now:          func() time.Time { return today },
	})
}

func date(s string) time.Time {
	t, _ := time.Parse(time.DateOnly, s)
	return t
}

func TestRunScrapesEveryProviderAndDayAndQueuesWhatItStores(t *testing.T) {
	f := newFixture()

	job, err := f.orchestrator().Run(context.Background(), "test", Request{Start: date("2026-10-01"), End: date("2026-10-02")})
	if err != nil {
		t.Fatal(err)
	}

	if job.Status != StatusSucceeded || job.Saved != (4+2)*2 {
		t.Fatalf("job = %+v", job)
	}
	if !slices.Equal(f.boe.days, []string{"2026-10-01", "2026-10-02"}) || len(f.boja.days) != 2 {
		t.Fatalf("days: BOE %v BOJA %v", f.boe.days, f.boja.days)
	}
	if len(f.publisher.messages) != 12 || len(f.storage.raws) != 12 {
		t.Fatalf("published %d, raw %d", len(f.publisher.messages), len(f.storage.raws))
	}
	msg := f.publisher.messages[0]
	if msg.BlobPath != "json/"+msg.Source+"/"+msg.DocumentID+".json" {
		t.Fatalf("blob path %q", msg.BlobPath)
	}
}

func TestOnlyDocumentsThatPassTheFiltersAreStoredAndCounted(t *testing.T) {
	f := newFixture()
	f.filters.rules = []*pb.FilterRule{{Provider: "BOE", FilterType: "Keyword", Value: "ayudas"}}

	job, err := f.orchestrator().Run(context.Background(), "test", Request{Providers: []string{"boe"}, Start: date("2026-10-01")})
	if err != nil {
		t.Fatal(err)
	}

	// Documents 1 and 3 have "ayudas" in the title.
	if job.Saved != 2 || len(f.storage.documents) != 2 || len(f.publisher.messages) != 2 {
		t.Fatalf("saved %d, stored %v", job.Saved, f.storage.documents)
	}
}

func TestFailedDownloadsAndStoresAreNotCounted(t *testing.T) {
	f := newFixture()
	f.boe.failIDs = map[string]bool{"BOE-20261001-1": true}

	job, _ := f.orchestrator().Run(context.Background(), "test", Request{Providers: []string{"BOE"}, Start: date("2026-10-01")})
	if job.Saved != 3 {
		t.Fatalf("saved = %d, want 3", job.Saved)
	}

	f2 := newFixture()
	f2.storage.failSave = true
	job, _ = f2.orchestrator().Run(context.Background(), "test", Request{Providers: []string{"BOE"}, Start: date("2026-10-01")})
	if job.Saved != 0 || len(f2.publisher.messages) != 0 {
		t.Fatalf("nothing stored must mean nothing counted or queued: %+v", job)
	}
}

func TestJobsFailClosedWithoutFilterRules(t *testing.T) {
	f := newFixture()
	f.filters.err = errors.New("api down")

	job, err := f.orchestrator().Run(context.Background(), "test", Request{Start: date("2026-10-01")})
	if err != nil {
		t.Fatal(err)
	}
	if job.Status != StatusFailed || len(f.boe.days) != 0 {
		t.Fatalf("job must fail before scraping: %+v, days %v", job, f.boe.days)
	}
}

func TestConfigurationRulesBecomeProviderOptions(t *testing.T) {
	f := newFixture()
	f.filters.rules = []*pb.FilterRule{
		{Provider: "BOJA", FilterType: "BojaFeed", Value: "https://boja/s53.xml"},
		{Provider: "BOE", FilterType: "Section", Value: "3"},
	}
	o := f.orchestrator()

	if _, err := o.Run(context.Background(), "test", Request{Start: date("2026-10-01")}); err != nil {
		t.Fatal(err)
	}
	if !slices.Equal(f.boja.options[0].Feeds, []string{"https://boja/s53.xml"}) || !slices.Equal(f.boe.options[0].Sections, []string{"3"}) {
		t.Fatalf("options: BOE %+v BOJA %+v", f.boe.options[0], f.boja.options[0])
	}

	// Sections in the request take precedence over the rules.
	if _, err := o.Run(context.Background(), "test", Request{Providers: []string{"BOE"}, Start: date("2026-10-01"), Sections: []string{"1"}}); err != nil {
		t.Fatal(err)
	}
	if !slices.Equal(f.boe.options[1].Sections, []string{"1"}) {
		t.Fatalf("sections = %v", f.boe.options[1].Sections)
	}
}

func TestRequestsAreValidated(t *testing.T) {
	o := newFixture().orchestrator()
	cases := map[string]Request{
		"no start":         {},
		"end before":       {Start: date("2026-10-05"), End: date("2026-10-01")},
		"future":           {Start: date("2026-10-14")},
		"range too long":   {Start: date("2026-09-01"), End: date("2026-09-30")},
		"unknown provider": {Providers: []string{"DOUE"}, Start: date("2026-10-01")},
	}
	for name, req := range cases {
		var invalid *ValidationError
		if _, err := o.Run(context.Background(), "test", req); !errors.As(err, &invalid) {
			t.Errorf("%s: err = %v, want a ValidationError", name, err)
		}
	}
}

func TestOneJobAtATimeAcrossTriggers(t *testing.T) {
	f := newFixture()
	f.boe.block = make(chan struct{})
	o := f.orchestrator()

	started, err := o.Start(context.Background(), "http", Request{Providers: []string{"BOE"}, Start: date("2026-10-01")})
	if err != nil || started.Status != StatusRunning || started.ID == "" {
		t.Fatalf("start: %+v %v", started, err)
	}
	if _, err := o.Run(context.Background(), "grpc", Request{Start: date("2026-10-01")}); !errors.Is(err, ErrBusy) {
		t.Fatalf("second job: err = %v, want ErrBusy", err)
	}

	close(f.boe.block)
	o.Wait()
	done, ok := o.Job(started.ID)
	if !ok || done.Status != StatusSucceeded || done.Saved != 4 || done.FinishedAt == nil {
		t.Fatalf("finished job = %+v", done)
	}
	if _, err := o.Run(context.Background(), "grpc", Request{Providers: []string{"BOE"}, Start: date("2026-10-01")}); err != nil {
		t.Fatalf("the lock must be released: %v", err)
	}
}

func TestCancellingTheContextStopsABackgroundJob(t *testing.T) {
	f := newFixture()
	f.boe.block = make(chan struct{})
	o := f.orchestrator()
	ctx, cancel := context.WithCancel(context.Background())

	job, err := o.Start(ctx, "http", Request{Providers: []string{"BOE"}, Start: date("2026-10-01"), End: date("2026-10-03")})
	if err != nil {
		t.Fatal(err)
	}
	cancel()
	o.Wait()

	stopped, _ := o.Job(job.ID)
	if stopped.Status != StatusFailed || len(f.boe.days) > 1 {
		t.Fatalf("job = %+v, days = %v", stopped, f.boe.days)
	}
}

func TestNextRun(t *testing.T) {
	loc := time.FixedZone("CET", 3600)
	cases := []struct{ now, want time.Time }{
		{time.Date(2026, 10, 13, 0, 30, 0, 0, loc), time.Date(2026, 10, 13, 1, 0, 0, 0, loc)},
		{time.Date(2026, 10, 13, 1, 0, 0, 0, loc), time.Date(2026, 10, 14, 1, 0, 0, 0, loc)},
		{time.Date(2026, 12, 31, 23, 0, 0, 0, loc), time.Date(2027, 1, 1, 1, 0, 0, 0, loc)},
	}
	for _, c := range cases {
		if got := nextRun(c.now, 1); !got.Equal(c.want) {
			t.Errorf("nextRun(%v) = %v, want %v", c.now, got, c.want)
		}
	}
}
