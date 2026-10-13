// Package pipeline runs scrape jobs: for each provider and day it lists the bulletin, downloads the
// documents that pass the filter rules, stores them and queues them for ingestion. One job runs at a time,
// whether it comes from the daily schedule, the HTTP API or the gRPC command.
package pipeline

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"errors"
	"fmt"
	"log/slog"
	"slices"
	"strings"
	"sync"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/messaging"
	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
	"github.com/asistente-ayuntamiento/go-scraper/internal/provider"
	"github.com/asistente-ayuntamiento/go-scraper/internal/storage"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/metric"
)

// ErrBusy is returned when a job is requested while another one runs.
var ErrBusy = errors.New("a scrape job is already running")

// ErrStopping is returned when a job is requested after Stop (the scraper is shutting down).
var ErrStopping = errors.New("the scraper is shutting down")

// ValidationError describes an invalid job request (bad dates, unknown provider, range too long).
type ValidationError struct{ Reason string }

func (e *ValidationError) Error() string { return e.Reason }

// FilterSource provides the active filter rules (the .NET API over gRPC).
type FilterSource interface {
	GetFilters(ctx context.Context) ([]*pb.FilterRule, error)
}

// Publisher queues stored documents for ingestion.
type Publisher interface {
	PublishDocument(ctx context.Context, msg messaging.DocumentMessage) error
}

// Request selects what a job scrapes. Dates are calendar days (inclusive); End defaults to Start.
type Request struct {
	Providers []string
	Start     time.Time
	End       time.Time
	// Sections of the BOE to fetch; empty uses the Section filter rules, then the provider defaults.
	Sections []string
}

type Status string

const (
	StatusRunning   Status = "running"
	StatusSucceeded Status = "succeeded"
	StatusFailed    Status = "failed"
)

// Job is a snapshot of a scrape job.
type Job struct {
	ID         string     `json:"id"`
	Trigger    string     `json:"trigger"`
	Providers  []string   `json:"providers"`
	Start      string     `json:"start"`
	End        string     `json:"end"`
	Status     Status     `json:"status"`
	Saved      int        `json:"saved"`
	Error      string     `json:"error,omitempty"`
	StartedAt  time.Time  `json:"started_at"`
	FinishedAt *time.Time `json:"finished_at,omitempty"`
}

// Config wires an Orchestrator.
type Config struct {
	Providers []provider.Provider
	Storage   storage.DocumentStorage
	// Publisher is optional: without it documents are stored but not queued.
	Publisher Publisher
	// Filters is required: jobs fail closed when the rules cannot be loaded.
	Filters      FilterSource
	MaxRangeDays int
	// Concurrency is the number of documents processed at once per day (default 5).
	Concurrency int
	Logger      *slog.Logger
	Now         func() time.Time
}

const keptJobs = 50

type Orchestrator struct {
	cfg       Config
	providers map[string]provider.Provider
	order     []string

	mu      sync.Mutex
	running bool
	jobs    map[string]*Job
	history []string

	// active tracks every running job, whatever started it (schedule, HTTP or gRPC).
	active sync.WaitGroup
	// stopped is cancelled by Stop: it cancels every job, even one running under a caller's context (gRPC).
	stopped    context.Context
	stopCancel context.CancelFunc
}

func New(cfg Config) *Orchestrator {
	if cfg.Concurrency <= 0 {
		cfg.Concurrency = 5
	}
	if cfg.MaxRangeDays <= 0 {
		cfg.MaxRangeDays = 31
	}
	if cfg.Logger == nil {
		cfg.Logger = slog.Default()
	}
	if cfg.Now == nil {
		cfg.Now = time.Now
	}
	o := &Orchestrator{cfg: cfg, providers: map[string]provider.Provider{}, jobs: map[string]*Job{}}
	o.stopped, o.stopCancel = context.WithCancel(context.Background())
	for _, p := range cfg.Providers {
		o.providers[strings.ToUpper(p.Name())] = p
		o.order = append(o.order, strings.ToUpper(p.Name()))
	}
	return o
}

// Run executes a job and returns when it ends. It fails with ErrBusy if another job is running.
func (o *Orchestrator) Run(ctx context.Context, trigger string, req Request) (Job, error) {
	req, err := o.validate(req)
	if err != nil {
		return Job{}, err
	}
	job, err := o.begin(trigger, req)
	if err != nil {
		return Job{}, err
	}
	o.active.Add(1)
	defer o.active.Done()
	o.execute(ctx, job, req)
	return o.snapshot(job.ID), nil
}

// Start launches a job in the background, under ctx (cancel it on shutdown), and returns its snapshot.
func (o *Orchestrator) Start(ctx context.Context, trigger string, req Request) (Job, error) {
	req, err := o.validate(req)
	if err != nil {
		return Job{}, err
	}
	job, err := o.begin(trigger, req)
	if err != nil {
		return Job{}, err
	}
	o.active.Add(1)
	go func() {
		defer o.active.Done()
		o.execute(ctx, job, req)
	}()
	return o.snapshot(job.ID), nil
}

// Job returns the snapshot of a recent job.
func (o *Orchestrator) Job(id string) (Job, bool) {
	o.mu.Lock()
	defer o.mu.Unlock()
	job, ok := o.jobs[id]
	if !ok {
		return Job{}, false
	}
	return *job, true
}

// Stop cancels every running job (they stop between documents and end as failed) and rejects new ones. Jobs started
// by gRPC run under the call's context, which shutdown does not cancel, so this is what stops them.
func (o *Orchestrator) Stop() { o.stopCancel() }

// Wait blocks until every job has returned (call after Stop).
func (o *Orchestrator) Wait() { o.active.Wait() }

// ProviderNames lists the registered providers in registration order.
func (o *Orchestrator) ProviderNames() []string { return slices.Clone(o.order) }

func (o *Orchestrator) validate(req Request) (Request, error) {
	if len(req.Providers) == 0 {
		req.Providers = o.ProviderNames()
	}
	for i, name := range req.Providers {
		name = strings.ToUpper(strings.TrimSpace(name))
		if _, ok := o.providers[name]; !ok {
			return req, &ValidationError{fmt.Sprintf("unknown provider %q (available: %s)", name, strings.Join(o.order, ", "))}
		}
		req.Providers[i] = name
	}

	if req.Start.IsZero() {
		return req, &ValidationError{"a start date is required"}
	}
	req.Start = day(req.Start)
	if req.End.IsZero() {
		req.End = req.Start
	}
	req.End = day(req.End)
	if req.End.Before(req.Start) {
		return req, &ValidationError{"the end date is before the start date"}
	}
	if req.End.After(day(o.cfg.Now())) {
		return req, &ValidationError{"the end date is in the future"}
	}
	if days := int(req.End.Sub(req.Start).Hours()/24) + 1; days > o.cfg.MaxRangeDays {
		return req, &ValidationError{fmt.Sprintf("the range has %d days; the maximum is %d", days, o.cfg.MaxRangeDays)}
	}
	return req, nil
}

func (o *Orchestrator) begin(trigger string, req Request) (*Job, error) {
	o.mu.Lock()
	defer o.mu.Unlock()
	if o.stopped.Err() != nil {
		return nil, ErrStopping
	}
	if o.running {
		return nil, ErrBusy
	}
	o.running = true

	job := &Job{
		ID:        newJobID(),
		Trigger:   trigger,
		Providers: slices.Clone(req.Providers),
		Start:     req.Start.Format(time.DateOnly),
		End:       req.End.Format(time.DateOnly),
		Status:    StatusRunning,
		StartedAt: o.cfg.Now(),
	}
	o.jobs[job.ID] = job
	o.history = append(o.history, job.ID)
	if len(o.history) > keptJobs {
		delete(o.jobs, o.history[0])
		o.history = o.history[1:]
	}
	return job, nil
}

func (o *Orchestrator) finish(job *Job, saved int, err error) {
	o.mu.Lock()
	defer o.mu.Unlock()
	finished := o.cfg.Now()
	job.FinishedAt = &finished
	job.Saved = saved
	job.Status = StatusSucceeded
	if err != nil {
		job.Status = StatusFailed
		job.Error = err.Error()
	}
	o.running = false
}

func (o *Orchestrator) snapshot(id string) Job {
	job, _ := o.Job(id)
	return job
}

func (o *Orchestrator) execute(ctx context.Context, job *Job, req Request) {
	ctx, cancel := context.WithCancel(ctx)
	defer cancel()
	stopJob := context.AfterFunc(o.stopped, cancel)
	defer stopJob()

	ctx, span := tracer.Start(ctx, "ScrapeJob")
	defer span.End()
	span.SetAttributes(
		attribute.String("job.id", job.ID),
		attribute.String("job.trigger", job.Trigger),
		attribute.StringSlice("job.providers", req.Providers),
		attribute.String("job.start", job.Start),
		attribute.String("job.end", job.End),
	)
	log := o.cfg.Logger.With("job_id", job.ID, "trigger", job.Trigger)
	log.InfoContext(ctx, "scrape job started", "providers", req.Providers, "start", job.Start, "end", job.End)

	saved, err := o.scrape(ctx, log, req)
	if err != nil {
		span.RecordError(err)
		span.SetStatus(codes.Error, err.Error())
		log.ErrorContext(ctx, "scrape job failed", "saved", saved, "error", err)
	} else {
		log.InfoContext(ctx, "scrape job finished", "saved", saved)
	}
	o.finish(job, saved, err)
}

func (o *Orchestrator) scrape(ctx context.Context, log *slog.Logger, req Request) (int, error) {
	// Fail closed: without the rules we cannot know what to discard, and "no rules" would mean
	// downloading everything.
	if o.cfg.Filters == nil {
		return 0, errors.New("the filter service is not configured")
	}
	rulesCtx, cancel := context.WithTimeout(ctx, 30*time.Second)
	rules, err := o.cfg.Filters.GetFilters(rulesCtx)
	cancel()
	if err != nil {
		return 0, fmt.Errorf("loading filter rules: %w", err)
	}

	saved := 0
	for _, name := range req.Providers {
		p := o.providers[name]
		opts := optionsFor(name, rules, req.Sections)
		for date := req.Start; !date.After(req.End); date = date.AddDate(0, 0, 1) {
			if err := ctx.Err(); err != nil {
				return saved, err
			}
			refs, err := p.FetchSummary(ctx, date, opts)
			if err != nil {
				// One missing day must not stop a range: log and go on.
				log.WarnContext(ctx, "summary failed", "provider", name, "date", date.Format(time.DateOnly), "error", err)
				summaryErrors.Add(ctx, 1, metric.WithAttributes(attribute.String("provider", name)))
				continue
			}
			log.InfoContext(ctx, "summary read", "provider", name, "date", date.Format(time.DateOnly), "documents", len(refs))
			saved += o.processDocuments(ctx, log, p, refs, rules)
		}
	}
	return saved, ctx.Err()
}

// processDocuments downloads, filters, stores and queues the documents; it returns how many were stored.
func (o *Orchestrator) processDocuments(ctx context.Context, log *slog.Logger, p provider.Provider, refs []provider.DocumentRef, rules []*pb.FilterRule) int {
	var (
		wg    sync.WaitGroup
		mu    sync.Mutex
		saved int
		slots = make(chan struct{}, o.cfg.Concurrency)
	)
	for _, ref := range refs {
		wg.Add(1)
		go func() {
			defer wg.Done()
			select {
			case slots <- struct{}{}:
			case <-ctx.Done():
				return
			}
			defer func() { <-slots }()

			if o.processDocument(ctx, log, p, ref, rules) {
				mu.Lock()
				saved++
				mu.Unlock()
			}
		}()
	}
	wg.Wait()
	return saved
}

func (o *Orchestrator) processDocument(ctx context.Context, log *slog.Logger, p provider.Provider, ref provider.DocumentRef, rules []*pb.FilterRule) bool {
	ctx, span := tracer.Start(ctx, "ProcessDocument")
	defer span.End()
	providerAttr := metric.WithAttributes(attribute.String("provider", p.Name()))
	fail := func(stage string, err error) {
		span.RecordError(err)
		span.SetStatus(codes.Error, stage)
		documentErrors.Add(ctx, 1, metric.WithAttributes(attribute.String("provider", p.Name()), attribute.String("stage", stage)))
		log.ErrorContext(ctx, "document failed", "provider", p.Name(), "ref", ref.ID, "stage", stage, "error", err)
	}

	doc, raw, err := p.FetchDocument(ctx, ref)
	if err != nil {
		fail("fetch", err)
		return false
	}
	span.SetAttributes(attribute.String("document.id", doc.DocumentID))

	if !matchesFilters(doc, rules) {
		documentsFiltered.Add(ctx, 1, providerAttr)
		log.DebugContext(ctx, "document discarded by the filters", "document_id", doc.DocumentID)
		return false
	}

	// The original is only a backup: a failure here does not lose the document.
	if err := o.cfg.Storage.SaveRaw(ctx, doc.Metadata.Source, doc.DocumentID, raw); err != nil {
		log.WarnContext(ctx, "raw backup failed", "document_id", doc.DocumentID, "error", err)
	}
	if err := o.cfg.Storage.SaveDocument(ctx, doc); err != nil {
		fail("store", err)
		return false
	}
	documentsSaved.Add(ctx, 1, providerAttr)

	if o.cfg.Publisher != nil {
		err := o.cfg.Publisher.PublishDocument(ctx, messaging.DocumentMessage{
			Source:     doc.Metadata.Source,
			DocumentID: doc.DocumentID,
			BlobPath:   storage.DocumentPath(doc.Metadata.Source, doc.DocumentID),
		})
		if err != nil {
			// Stored but not queued: the admin "reprocess" action can still queue it.
			fail("publish", err)
		}
	}
	log.InfoContext(ctx, "document saved", "document_id", doc.DocumentID)
	return true
}

// optionsFor builds the provider options from the request and the configuration rules (BojaFeed, Section).
func optionsFor(providerName string, rules []*pb.FilterRule, requestedSections []string) provider.Options {
	var opts provider.Options
	for _, rule := range rules {
		if !strings.EqualFold(rule.Provider, providerName) || strings.TrimSpace(rule.Value) == "" {
			continue
		}
		switch rule.FilterType {
		case "BojaFeed":
			opts.Feeds = append(opts.Feeds, strings.TrimSpace(rule.Value))
		case "Section":
			opts.Sections = append(opts.Sections, strings.TrimSpace(rule.Value))
		}
	}
	if len(requestedSections) > 0 {
		opts.Sections = requestedSections
	}
	return opts
}

func newJobID() string {
	b := make([]byte, 8)
	_, _ = rand.Read(b)
	return hex.EncodeToString(b)
}

func day(t time.Time) time.Time {
	return time.Date(t.Year(), t.Month(), t.Day(), 0, 0, 0, 0, time.UTC)
}

var (
	tracer = otel.Tracer("go-scraper/pipeline")
	meter  = otel.Meter("go-scraper")

	documentsSaved, _    = meter.Int64Counter("scraper.documents.saved", metric.WithDescription("Documents stored after passing the filters"))
	documentsFiltered, _ = meter.Int64Counter("scraper.documents.filtered", metric.WithDescription("Documents discarded by the filter rules"))
	documentErrors, _    = meter.Int64Counter("scraper.documents.errors", metric.WithDescription("Documents that failed, by stage (fetch, store, publish)"))
	summaryErrors, _     = meter.Int64Counter("scraper.summaries.errors", metric.WithDescription("Bulletin summaries that could not be read"))
)
