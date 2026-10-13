// Package provider defines the contract of an official bulletin source (BOE, BOJA).
// Providers are stateless: what to fetch comes in Options on every call, so one instance can serve
// concurrent jobs.
package provider

import (
	"context"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
)

// Options select what part of a bulletin is fetched. Empty fields use the provider defaults.
type Options struct {
	// Sections of the BOE summary (for example "1", "3", "5A").
	Sections []string
	// Feeds are the BOJA Atom feeds that list today's bulletin.
	Feeds []string
}

// DocumentRef identifies a document found in a summary and the bulletin date it was published in.
type DocumentRef struct {
	ID          string
	PublishedOn time.Time
}

// Raw is the original document as downloaded, kept as a backup next to the parsed JSON.
type Raw struct {
	Data        []byte
	Extension   string
	ContentType string
}

type Provider interface {
	// Name is the bulletin code used in blob paths, messages and logs ("BOE", "BOJA").
	Name() string

	// FetchSummary lists the documents published on date (a calendar day; the time is ignored).
	FetchSummary(ctx context.Context, date time.Time, opts Options) ([]DocumentRef, error)

	// FetchDocument downloads and parses one document.
	FetchDocument(ctx context.Context, ref DocumentRef) (*scraper.Document, Raw, error)
}
