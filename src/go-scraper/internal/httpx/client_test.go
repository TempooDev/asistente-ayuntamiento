package httpx

import (
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"sync/atomic"
	"testing"
	"time"
)

func fastClient() *Client {
	return New(Options{RequestsPerSecond: 1000, Burst: 10, BaseDelay: time.Millisecond, MaxAttempts: 3})
}

func server(t *testing.T, handler http.HandlerFunc) string {
	t.Helper()
	srv := httptest.NewServer(handler)
	t.Cleanup(srv.Close)
	return srv.URL
}

func TestRetriesServerErrorsThenSucceeds(t *testing.T) {
	var calls atomic.Int32
	url := server(t, func(w http.ResponseWriter, r *http.Request) {
		if calls.Add(1) < 3 {
			w.WriteHeader(http.StatusTooManyRequests)
			return
		}
		w.Write([]byte("ok"))
	})

	body, err := fastClient().Get(context.Background(), url, nil)
	if err != nil || string(body) != "ok" || calls.Load() != 3 {
		t.Fatalf("body=%q err=%v calls=%d", body, err, calls.Load())
	}
}

func TestClientErrorsAreNotRetried(t *testing.T) {
	var calls atomic.Int32
	url := server(t, func(w http.ResponseWriter, r *http.Request) {
		calls.Add(1)
		http.NotFound(w, r)
	})

	_, err := fastClient().Get(context.Background(), url, nil)
	if !IsNotFound(err) || calls.Load() != 1 {
		t.Fatalf("err=%v calls=%d", err, calls.Load())
	}
}

func TestGivesUpAfterMaxAttempts(t *testing.T) {
	var calls atomic.Int32
	url := server(t, func(w http.ResponseWriter, r *http.Request) {
		calls.Add(1)
		w.WriteHeader(http.StatusBadGateway)
	})

	_, err := fastClient().Get(context.Background(), url, nil)
	var statusErr *StatusError
	if !errors.As(err, &statusErr) || statusErr.StatusCode != http.StatusBadGateway || calls.Load() != 3 {
		t.Fatalf("err=%v calls=%d", err, calls.Load())
	}
}

func TestCancellationStopsTheBackoff(t *testing.T) {
	url := server(t, func(w http.ResponseWriter, r *http.Request) { w.WriteHeader(http.StatusServiceUnavailable) })
	client := New(Options{RequestsPerSecond: 1000, BaseDelay: time.Hour, MaxAttempts: 3})
	ctx, cancel := context.WithTimeout(context.Background(), 50*time.Millisecond)
	defer cancel()

	start := time.Now()
	_, err := client.Get(ctx, url, nil)
	if !errors.Is(err, context.DeadlineExceeded) || time.Since(start) > 5*time.Second {
		t.Fatalf("err=%v after %v", err, time.Since(start))
	}
}

func TestHeadersAreSent(t *testing.T) {
	url := server(t, func(w http.ResponseWriter, r *http.Request) { w.Write([]byte(r.Header.Get("Accept"))) })

	body, err := fastClient().Get(context.Background(), url, map[string]string{"Accept": "application/json"})
	if err != nil || string(body) != "application/json" {
		t.Fatalf("body=%q err=%v", body, err)
	}
}

func TestOversizedBodiesAreRejected(t *testing.T) {
	url := server(t, func(w http.ResponseWriter, r *http.Request) { w.Write(make([]byte, 2048)) })

	_, err := New(Options{RequestsPerSecond: 1000, MaxBodyBytes: 1024}).Get(context.Background(), url, nil)
	if err == nil {
		t.Fatal("expected an error")
	}
}
