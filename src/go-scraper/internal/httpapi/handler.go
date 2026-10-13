// Package httpapi serves the scraper's HTTP endpoints: health, manual scrape jobs and their status.
package httpapi

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"strings"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/pipeline"
)

// Jobs is the part of the orchestrator the API needs.
type Jobs interface {
	Start(ctx context.Context, trigger string, req pipeline.Request) (pipeline.Job, error)
	Job(id string) (pipeline.Job, bool)
}

// NewHandler routes the API. Jobs started here run under jobCtx (cancelled on shutdown), not the request.
func NewHandler(jobCtx context.Context, jobs Jobs) http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", func(w http.ResponseWriter, r *http.Request) {
		w.Write([]byte("BOE Scraper is running\n"))
	})
	mux.HandleFunc("/api/scrape", func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodGet && r.Method != http.MethodPost {
			w.Header().Set("Allow", "GET, POST")
			writeError(w, http.StatusMethodNotAllowed, "use GET or POST")
			return
		}
		startScrape(jobCtx, jobs, w, r)
	})
	mux.HandleFunc("GET /api/jobs/{id}", func(w http.ResponseWriter, r *http.Request) {
		job, ok := jobs.Job(r.PathValue("id"))
		if !ok {
			writeError(w, http.StatusNotFound, "unknown job")
			return
		}
		writeJSON(w, http.StatusOK, job)
	})
	return mux
}

// startScrape: ?start=YYYY-MM-DD (required) &end=YYYY-MM-DD &provider=BOE,BOJA &section=1,3.
// Answers 202 with the job and a Location header to poll, 409 if a job is running, 400 if invalid.
func startScrape(jobCtx context.Context, jobs Jobs, w http.ResponseWriter, r *http.Request) {
	query := r.URL.Query()
	start, err := parseDate(query.Get("start"))
	if err != nil || start.IsZero() {
		writeError(w, http.StatusBadRequest, "the 'start' parameter (YYYY-MM-DD) is required")
		return
	}
	end, err := parseDate(query.Get("end"))
	if err != nil {
		writeError(w, http.StatusBadRequest, "invalid 'end' (YYYY-MM-DD)")
		return
	}

	job, err := jobs.Start(jobCtx, "http", pipeline.Request{
		Providers: list(query.Get("provider")),
		Start:     start,
		End:       end,
		Sections:  list(query.Get("section")),
	})
	var invalid *pipeline.ValidationError
	switch {
	case errors.Is(err, pipeline.ErrBusy):
		writeError(w, http.StatusConflict, err.Error())
		return
	case errors.As(err, &invalid):
		writeError(w, http.StatusBadRequest, invalid.Reason)
		return
	case err != nil:
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}

	w.Header().Set("Location", "/api/jobs/"+job.ID)
	writeJSON(w, http.StatusAccepted, job)
}

func parseDate(value string) (time.Time, error) {
	if value == "" {
		return time.Time{}, nil
	}
	return time.Parse(time.DateOnly, value)
}

func list(value string) []string {
	var out []string
	for _, item := range strings.Split(value, ",") {
		if item = strings.TrimSpace(item); item != "" {
			out = append(out, item)
		}
	}
	return out
}

func writeJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(body)
}

func writeError(w http.ResponseWriter, status int, message string) {
	writeJSON(w, status, map[string]string{"error": message})
}
