// Package boja reads the Boletín Oficial de la Junta de Andalucía: today's bulletin from its Atom feeds and
// past bulletins by a binary search over the numbered bulletin index pages.
package boja

import (
	"context"
	"encoding/xml"
	"fmt"
	"regexp"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/httpx"
	"github.com/asistente-ayuntamiento/go-scraper/internal/provider"
	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
)

const (
	Name           = "BOJA"
	DefaultBaseURL = "https://www.juntadeandalucia.es"
	department     = "Junta de Andalucía"
	// maxBulletinsPerYear bounds the search when the feeds do not reveal the latest number.
	maxBulletinsPerYear = 300
)

var tracer = otel.Tracer("go-scraper/boja")

var (
	// bulletinPath matches /boja/<year>/<number>/ in a feed or document URL.
	bulletinPath = regexp.MustCompile(`/boja/(\d{4})/(\d+)/`)
	// bulletinDate matches the heading of an index page: "BOJA nº 130 de 08/07/2026" (extraordinary: "130.1").
	bulletinDate = regexp.MustCompile(`BOJA nº \d+(?:\.\d+)? de (\d{2}/\d{2}/\d{4})`)
	// documentLink matches absolute or relative document links of an index page.
	documentLink = regexp.MustCompile(`href="((?:https?://[^"/]+)?/boja/(\d{4})/(\d+)/\d+(?:\.html)?)"`)
	titleTag     = regexp.MustCompile(`(?is)<title>(.*?)</title>`)
	whitespace   = regexp.MustCompile(`\s+`)
)

type Provider struct {
	client       *httpx.Client
	baseURL      string
	defaultFeeds []string
	now          func() time.Time

	// dates caches the publication date of each bulletin (year -> number -> date); dates never change.
	mu    sync.Mutex
	dates map[int]map[int]time.Time
}

// NewProvider uses baseURL for every request ("" means DefaultBaseURL) and defaultFeeds when a call passes none.
func NewProvider(client *httpx.Client, baseURL string, defaultFeeds []string) *Provider {
	if baseURL == "" {
		baseURL = DefaultBaseURL
	}
	baseURL = strings.TrimRight(baseURL, "/")
	if len(defaultFeeds) == 0 {
		for section := 51; section <= 55; section++ {
			defaultFeeds = append(defaultFeeds, fmt.Sprintf("%s/boja/distribucion/s%d.xml", baseURL, section))
		}
	}
	return &Provider{client: client, baseURL: baseURL, defaultFeeds: defaultFeeds, now: time.Now, dates: map[int]map[int]time.Time{}}
}

// NewHTTPClient is the rate-limited client for juntadeandalucia.es (1 request per second).
func NewHTTPClient() *httpx.Client {
	return httpx.New(httpx.Options{RequestsPerSecond: 1, Burst: 1})
}

func (p *Provider) Name() string { return Name }

// FetchSummary lists the documents of the bulletin published on date. The feeds only describe the latest
// bulletin and carry no date, so they are used for today; other days are found in the index pages.
func (p *Provider) FetchSummary(ctx context.Context, date time.Time, opts provider.Options) ([]provider.DocumentRef, error) {
	ctx, span := tracer.Start(ctx, "boja.FetchSummary")
	defer span.End()
	span.SetAttributes(attribute.String("bulletin.date", date.Format(time.DateOnly)))

	feeds := opts.Feeds
	if len(feeds) == 0 {
		feeds = p.defaultFeeds
	}

	var urls []string
	var err error
	if sameDay(date, p.now()) {
		urls, err = p.latestFromFeeds(ctx, feeds)
	} else {
		urls, err = p.searchBulletin(ctx, feeds, day(date))
	}
	if err != nil {
		return nil, err
	}

	refs := make([]provider.DocumentRef, 0, len(urls))
	for _, url := range unique(urls) {
		refs = append(refs, provider.DocumentRef{ID: url, PublishedOn: day(date)})
	}
	return refs, nil
}

// latestFromFeeds returns the document links of every feed. A failing feed is skipped unless all fail.
func (p *Provider) latestFromFeeds(ctx context.Context, feeds []string) ([]string, error) {
	var urls []string
	var lastErr error
	for _, feed := range feeds {
		body, err := p.client.Get(ctx, feed, nil)
		if err != nil {
			lastErr = err
			continue
		}
		var atom struct {
			Entries []struct {
				Link struct {
					Href string `xml:"href,attr"`
				} `xml:"link"`
			} `xml:"entry"`
		}
		if err := xml.Unmarshal(body, &atom); err != nil {
			lastErr = fmt.Errorf("BOJA feed %s: %w", feed, err)
			continue
		}
		for _, entry := range atom.Entries {
			if entry.Link.Href != "" {
				urls = append(urls, entry.Link.Href)
			}
		}
	}
	if len(urls) == 0 && lastErr != nil {
		return nil, fmt.Errorf("BOJA feeds: %w", lastErr)
	}
	return urls, nil
}

// searchBulletin binary-searches the bulletin numbers of target's year for the one published on target.
func (p *Provider) searchBulletin(ctx context.Context, feeds []string, target time.Time) ([]string, error) {
	year := target.Year()
	high := p.latestNumber(ctx, feeds, year)

	// The highest number may not exist (yet); walk down to the latest published one.
	for ; high > 0; high-- {
		if _, _, err := p.bulletin(ctx, year, high, target); err == nil {
			break
		} else if !httpx.IsNotFound(err) {
			return nil, err
		}
	}

	low := 1
	for low <= high {
		mid := (low + high) / 2
		date, links, err := p.bulletin(ctx, year, mid, target)
		if httpx.IsNotFound(err) {
			// A gap in the numbering: use the closest published bulletin below mid.
			for mid--; mid >= low; mid-- {
				if date, links, err = p.bulletin(ctx, year, mid, target); !httpx.IsNotFound(err) {
					break
				}
			}
			if mid < low {
				low = (low+high)/2 + 1
				continue
			}
		}
		if err != nil {
			return nil, err
		}

		switch {
		case date.Equal(target):
			return links, nil
		case date.Before(target):
			low = mid + 1
		default:
			high = mid - 1
		}
	}
	return nil, nil // No bulletin that day (weekends, holidays).
}

// latestNumber is the number of the bulletin in the feeds when it belongs to year, or maxBulletinsPerYear.
func (p *Provider) latestNumber(ctx context.Context, feeds []string, year int) int {
	urls, err := p.latestFromFeeds(ctx, feeds)
	if err != nil {
		return maxBulletinsPerYear
	}
	for _, url := range urls {
		if m := bulletinPath.FindStringSubmatch(url); m != nil && m[1] == strconv.Itoa(year) {
			if n, err := strconv.Atoi(m[2]); err == nil {
				return n
			}
		}
	}
	return maxBulletinsPerYear
}

// bulletin returns the date of bulletin number num, and its document links when that date is target.
// Dates are cached, so pages of other days are downloaded once.
func (p *Provider) bulletin(ctx context.Context, year, num int, target time.Time) (time.Time, []string, error) {
	if date, ok := p.cachedDate(year, num); ok && !date.Equal(target) {
		return date, nil, nil
	}

	body, err := p.client.Get(ctx, fmt.Sprintf("%s/boja/%d/%d/index.html", p.baseURL, year, num), nil)
	if err != nil {
		return time.Time{}, nil, err
	}
	page := string(body)

	m := bulletinDate.FindStringSubmatch(page)
	if m == nil {
		return time.Time{}, nil, fmt.Errorf("BOJA %d/%d: no publication date in the index page", year, num)
	}
	date, err := time.Parse("02/01/2006", m[1])
	if err != nil {
		return time.Time{}, nil, fmt.Errorf("BOJA %d/%d: %w", year, num, err)
	}
	p.storeDate(year, num, date)

	if !date.Equal(target) {
		return date, nil, nil
	}
	var links []string
	for _, link := range documentLink.FindAllStringSubmatch(page, -1) {
		if link[2] != strconv.Itoa(year) || link[3] != strconv.Itoa(num) {
			continue
		}
		url := link[1]
		if strings.HasPrefix(url, "/") {
			url = p.baseURL + url
		}
		links = append(links, url)
	}
	return date, unique(links), nil
}

func (p *Provider) cachedDate(year, num int) (time.Time, bool) {
	p.mu.Lock()
	defer p.mu.Unlock()
	date, ok := p.dates[year][num]
	return date, ok
}

func (p *Provider) storeDate(year, num int, date time.Time) {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.dates[year] == nil {
		p.dates[year] = map[int]time.Time{}
	}
	p.dates[year][num] = date
}

// FetchDocument downloads a document page. Its id derives from the URL (/boja/2026/123/1 -> BOJA-2026-123-1)
// and its publication date is the date of the bulletin it was listed in.
func (p *Provider) FetchDocument(ctx context.Context, ref provider.DocumentRef) (*scraper.Document, provider.Raw, error) {
	ctx, span := tracer.Start(ctx, "boja.FetchDocument")
	defer span.End()
	span.SetAttributes(attribute.String("document.url", ref.ID))

	data, err := p.client.Get(ctx, ref.ID, nil)
	if err != nil {
		return nil, provider.Raw{}, fmt.Errorf("BOJA document %s: %w", ref.ID, err)
	}
	page := string(data)

	id := DocumentID(ref.ID)
	title := "Documento BOJA"
	if m := titleTag.FindStringSubmatch(page); m != nil {
		if t := strings.TrimSpace(whitespace.ReplaceAllString(m[1], " ")); t != "" {
			title = t
		}
	}
	published := ""
	if !ref.PublishedOn.IsZero() {
		published = ref.PublishedOn.Format(time.DateOnly)
	}

	return &scraper.Document{
		DocumentID: id,
		Metadata: scraper.Metadata{
			Source:           Name,
			DocumentID:       id,
			Titulo:           title,
			Departamento:     department,
			FechaPublicacion: published,
		},
		Text: scraper.StripHTMLTags(page),
	}, provider.Raw{Data: data, Extension: "html", ContentType: "text/html; charset=utf-8"}, nil
}

// DocumentID derives a stable id from a document URL: .../boja/2026/123/1 -> BOJA-2026-123-1.
func DocumentID(url string) string {
	parts := strings.Split(strings.TrimRight(url, "/"), "/")
	if len(parts) < 3 {
		return url
	}
	return strings.TrimSuffix("BOJA-"+strings.Join(parts[len(parts)-3:], "-"), ".html")
}

func sameDay(a, b time.Time) bool {
	return a.Year() == b.Year() && a.YearDay() == b.YearDay()
}

func day(t time.Time) time.Time {
	return time.Date(t.Year(), t.Month(), t.Day(), 0, 0, 0, 0, time.UTC)
}

func unique(values []string) []string {
	seen := make(map[string]bool, len(values))
	out := values[:0:0]
	for _, v := range values {
		if !seen[v] {
			seen[v] = true
			out = append(out, v)
		}
	}
	return out
}
