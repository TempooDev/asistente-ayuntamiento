package boja

import (
	"context"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"regexp"
	"strconv"
	"strings"
	"testing"
	"time"
)

const latestNumber = 40

// bulletinDate is the publication date of bulletin num in the fake site: one every two days from 2 January.
func bulletinDate(year, num int) time.Time {
	return time.Date(year, 1, 2, 0, 0, 0, 0, time.UTC).AddDate(0, 0, 2*(num-1))
}

var indexPath = regexp.MustCompile(`^/boja/(\d{4})/(\d+)/index\.html$`)

// fakeSite emulates juntadeandalucia.es: Atom feeds with the latest bulletin and one index page per bulletin.
// Odd bulletins link their documents with absolute URLs, even ones with relative URLs.
func fakeSite(t *testing.T, year int) (requests map[string]int) {
	t.Helper()
	requests = map[string]int{}
	var srv *httptest.Server
	srv = httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		requests[r.URL.Path]++
		if strings.HasPrefix(r.URL.Path, "/boja/distribucion/") {
			fmt.Fprintf(w, `<feed xmlns="http://www.w3.org/2005/Atom"><entry><link href="%s/boja/%d/%d/1"/></entry><entry><link href="%s/boja/%d/%d/2"/></entry></feed>`,
				srv.URL, year, latestNumber, srv.URL, year, latestNumber)
			return
		}
		m := indexPath.FindStringSubmatch(r.URL.Path)
		if m == nil {
			http.NotFound(w, r)
			return
		}
		num, _ := strconv.Atoi(m[2])
		if num > latestNumber {
			http.NotFound(w, r)
			return
		}
		link := func(doc int) string {
			if num%2 == 0 {
				return fmt.Sprintf("/boja/%d/%d/%d", year, num, doc)
			}
			return fmt.Sprintf("%s/boja/%d/%d/%d", srv.URL, year, num, doc)
		}
		fmt.Fprintf(w, `<html><p class="titular">BOJA nº %d de %s</p><a href="%s">1</a><a href="%s">2</a><a href="%s">dup</a></html>`,
			num, bulletinDate(year, num).Format("02/01/2006"), link(1), link(2), link(1))
	}))
	t.Cleanup(srv.Close)

	old := baseURL
	baseURL = srv.URL
	t.Cleanup(func() { baseURL = old })
	return requests
}

func TestHistoricSummaryFindsTheBulletinOfThatDateByBinarySearch(t *testing.T) {
	year := time.Now().Year()
	requests := fakeSite(t, year)

	for _, num := range []int{17, 18, 1, latestNumber} {
		urls, err := NewProvider().FetchSummary(context.Background(), bulletinDate(year, num))
		if err != nil {
			t.Fatal(err)
		}
		want := []string{fmt.Sprintf("/boja/%d/%d/1", year, num), fmt.Sprintf("/boja/%d/%d/2", year, num)}
		if len(urls) != 2 || !strings.HasSuffix(urls[0], want[0]) || !strings.HasSuffix(urls[1], want[1]) {
			t.Fatalf("bulletin %d: urls = %v, want suffixes %v", num, urls, want)
		}
		if !strings.HasPrefix(urls[0], baseURL) {
			t.Fatalf("bulletin %d: relative links must be made absolute: %v", num, urls)
		}
	}

	// A binary search over 40 bulletins needs far fewer than 40 index downloads per lookup.
	indexDownloads := 0
	for path, count := range requests {
		if indexPath.MatchString(path) {
			indexDownloads += count
		}
	}
	if indexDownloads > 4*12 {
		t.Fatalf("too many index downloads: %d", indexDownloads)
	}
}

func TestHistoricSummaryWithoutBulletinThatDayIsEmpty(t *testing.T) {
	year := time.Now().Year()
	fakeSite(t, year)

	// Bulletins are published every two days: the day after bulletin 10 has none.
	urls, err := NewProvider().FetchSummary(context.Background(), bulletinDate(year, 10).AddDate(0, 0, 1))
	if err != nil {
		t.Fatal(err)
	}
	if len(urls) != 0 {
		t.Fatalf("urls = %v, want none", urls)
	}
}

func TestTodaySummaryReadsTheAtomFeeds(t *testing.T) {
	year := time.Now().Year()
	requests := fakeSite(t, year)
	today := time.Now()

	urls, err := NewProvider().FetchSummary(context.Background(), time.Date(today.Year(), today.Month(), today.Day(), 0, 0, 0, 0, time.UTC))
	if err != nil {
		t.Fatal(err)
	}

	// Five default feeds, two entries each (duplicates across feeds are kept).
	if len(urls) != 10 {
		t.Fatalf("got %d urls: %v", len(urls), urls)
	}
	if requests["/boja/distribucion/s51.xml"] != 1 || requests["/boja/distribucion/s55.xml"] != 1 {
		t.Fatalf("feeds not requested: %v", requests)
	}
}

func TestFetchDocumentDerivesIdAndTitle(t *testing.T) {
	page, err := os.ReadFile("testdata/documento.html")
	if err != nil {
		t.Fatal(err)
	}
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.Write(page) }))
	t.Cleanup(srv.Close)

	doc, raw, err := NewProvider().FetchDocument(context.Background(), srv.URL+"/boja/2026/17/3")
	if err != nil {
		t.Fatal(err)
	}

	if doc.DocumentID != "BOJA-2026-17-3" || doc.Metadata.DocumentID != doc.DocumentID {
		t.Fatalf("id = %q", doc.DocumentID)
	}
	if doc.Metadata.Titulo != "BOJA nº 17 - Resolución de ayudas a la vivienda" || doc.Metadata.Departamento != "Junta de Andalucía" || doc.Metadata.Source != "BOJA" {
		t.Fatalf("metadata = %+v", doc.Metadata)
	}
	if !strings.Contains(doc.Text, "ayudas  para el alquiler & la rehabilitación") || strings.Contains(doc.Text, "<p>") {
		t.Fatalf("text = %q", doc.Text)
	}
	if string(raw) != string(page) {
		t.Fatal("raw HTML must be returned unchanged")
	}
}
