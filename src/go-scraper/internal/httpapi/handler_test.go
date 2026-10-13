package httpapi

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/asistente-ayuntamiento/go-scraper/internal/pipeline"
)

type fakeJobs struct {
	err  error
	last pipeline.Request
	jobs map[string]pipeline.Job
}

func (f *fakeJobs) Start(ctx context.Context, trigger string, req pipeline.Request) (pipeline.Job, error) {
	f.last = req
	if f.err != nil {
		return pipeline.Job{}, f.err
	}
	return pipeline.Job{ID: "abc", Trigger: trigger, Status: pipeline.StatusRunning}, nil
}

func (f *fakeJobs) Job(id string) (pipeline.Job, bool) {
	job, ok := f.jobs[id]
	return job, ok
}

func serve(jobs *fakeJobs, method, target string) *httptest.ResponseRecorder {
	rec := httptest.NewRecorder()
	NewHandler(context.Background(), jobs).ServeHTTP(rec, httptest.NewRequest(method, target, nil))
	return rec
}

func TestScrapeStartsAJobAndPointsToItsStatus(t *testing.T) {
	jobs := &fakeJobs{}

	rec := serve(jobs, http.MethodPost, "/api/scrape?start=2026-10-01&end=2026-10-02&provider=BOE,BOJA&section=1")

	if rec.Code != http.StatusAccepted || rec.Header().Get("Location") != "/api/jobs/abc" {
		t.Fatalf("status %d location %q", rec.Code, rec.Header().Get("Location"))
	}
	if len(jobs.last.Providers) != 2 || jobs.last.Sections[0] != "1" || jobs.last.End.Format("2006-01-02") != "2026-10-02" {
		t.Fatalf("request = %+v", jobs.last)
	}
	var job pipeline.Job
	if err := json.NewDecoder(rec.Body).Decode(&job); err != nil || job.ID != "abc" {
		t.Fatalf("body: %v %+v", err, job)
	}
}

func TestScrapeErrorsMapToStatusCodes(t *testing.T) {
	cases := []struct {
		target string
		err    error
		want   int
	}{
		{"/api/scrape", nil, http.StatusBadRequest},
		{"/api/scrape?start=01/10/2026", nil, http.StatusBadRequest},
		{"/api/scrape?start=2026-10-01", pipeline.ErrBusy, http.StatusConflict},
		{"/api/scrape?start=2026-10-01", &pipeline.ValidationError{Reason: "too long"}, http.StatusBadRequest},
	}
	for _, c := range cases {
		if rec := serve(&fakeJobs{err: c.err}, http.MethodGet, c.target); rec.Code != c.want {
			t.Errorf("%s with %v: status %d, want %d", c.target, c.err, rec.Code, c.want)
		}
	}
	if rec := serve(&fakeJobs{}, http.MethodDelete, "/api/scrape?start=2026-10-01"); rec.Code != http.StatusMethodNotAllowed {
		t.Errorf("DELETE: status %d", rec.Code)
	}
}

func TestJobStatus(t *testing.T) {
	jobs := &fakeJobs{jobs: map[string]pipeline.Job{"abc": {ID: "abc", Status: pipeline.StatusSucceeded, Saved: 7}}}

	if rec := serve(jobs, http.MethodGet, "/api/jobs/abc"); rec.Code != http.StatusOK {
		t.Fatalf("status %d", rec.Code)
	}
	if rec := serve(jobs, http.MethodGet, "/api/jobs/zzz"); rec.Code != http.StatusNotFound {
		t.Fatalf("status %d", rec.Code)
	}
	if rec := serve(jobs, http.MethodGet, "/health"); rec.Code != http.StatusOK {
		t.Fatalf("health %d", rec.Code)
	}
}
