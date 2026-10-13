// Package httpx is the shared HTTP client of the providers: per-site rate limit, timeout, and retries
// with exponential backoff and jitter that stop as soon as the context is cancelled.
package httpx

import (
	"context"
	"errors"
	"fmt"
	"io"
	"math/rand/v2"
	"net/http"
	"time"

	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"golang.org/x/time/rate"
)

// StatusError is returned for non-2xx responses after retries (if any) are exhausted.
type StatusError struct {
	URL        string
	StatusCode int
}

func (e *StatusError) Error() string { return fmt.Sprintf("GET %s: HTTP %d", e.URL, e.StatusCode) }

// IsNotFound reports whether err is a 404 from the server.
func IsNotFound(err error) bool {
	var statusErr *StatusError
	return errors.As(err, &statusErr) && statusErr.StatusCode == http.StatusNotFound
}

// Options configure a Client. Zero values take the defaults.
type Options struct {
	// RequestsPerSecond is the sustained rate towards the site (default 1).
	RequestsPerSecond float64
	// Burst is the number of requests allowed at once (default 1).
	Burst int
	// Timeout bounds each attempt, including reading the body (default 30s).
	Timeout time.Duration
	// MaxAttempts includes the first try (default 3).
	MaxAttempts int
	// BaseDelay is the backoff before the second attempt; it doubles each time, with jitter (default 1s).
	BaseDelay time.Duration
	// MaxBodyBytes protects against oversized responses (default 50 MiB).
	MaxBodyBytes int64
}

// Client is safe for concurrent use.
type Client struct {
	http    *http.Client
	limiter *rate.Limiter
	opts    Options
}

func New(opts Options) *Client {
	if opts.RequestsPerSecond <= 0 {
		opts.RequestsPerSecond = 1
	}
	if opts.Burst <= 0 {
		opts.Burst = 1
	}
	if opts.Timeout <= 0 {
		opts.Timeout = 30 * time.Second
	}
	if opts.MaxAttempts <= 0 {
		opts.MaxAttempts = 3
	}
	if opts.BaseDelay <= 0 {
		opts.BaseDelay = time.Second
	}
	if opts.MaxBodyBytes <= 0 {
		opts.MaxBodyBytes = 50 << 20
	}
	return &Client{
		http:    &http.Client{Transport: otelhttp.NewTransport(http.DefaultTransport), Timeout: opts.Timeout},
		limiter: rate.NewLimiter(rate.Limit(opts.RequestsPerSecond), opts.Burst),
		opts:    opts,
	}
}

// Get returns the body of a 2xx response. Network errors, 429 and 5xx are retried; other statuses
// fail immediately with a *StatusError.
func (c *Client) Get(ctx context.Context, url string, headers map[string]string) ([]byte, error) {
	var lastErr error
	for attempt := 1; attempt <= c.opts.MaxAttempts; attempt++ {
		if attempt > 1 {
			if err := sleep(ctx, c.backoff(attempt)); err != nil {
				return nil, err
			}
		}
		if err := c.limiter.Wait(ctx); err != nil {
			return nil, err
		}

		body, retry, err := c.do(ctx, url, headers)
		if err == nil {
			return body, nil
		}
		if !retry || ctx.Err() != nil {
			return nil, err
		}
		lastErr = err
	}
	return nil, fmt.Errorf("after %d attempts: %w", c.opts.MaxAttempts, lastErr)
}

func (c *Client) do(ctx context.Context, url string, headers map[string]string) (body []byte, retry bool, err error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, false, err
	}
	for key, value := range headers {
		req.Header.Set(key, value)
	}

	resp, err := c.http.Do(req)
	if err != nil {
		return nil, true, err
	}
	defer func() {
		// Drain so the connection can be reused, then close.
		_, _ = io.Copy(io.Discard, io.LimitReader(resp.Body, 64<<10))
		resp.Body.Close()
	}()

	if resp.StatusCode < 200 || resp.StatusCode > 299 {
		retryable := resp.StatusCode == http.StatusTooManyRequests || resp.StatusCode >= 500
		return nil, retryable, &StatusError{URL: url, StatusCode: resp.StatusCode}
	}

	body, err = io.ReadAll(io.LimitReader(resp.Body, c.opts.MaxBodyBytes+1))
	if err != nil {
		return nil, true, err
	}
	if int64(len(body)) > c.opts.MaxBodyBytes {
		return nil, false, fmt.Errorf("GET %s: body larger than %d bytes", url, c.opts.MaxBodyBytes)
	}
	return body, false, nil
}

// backoff is BaseDelay * 2^(attempt-2), plus up to 50% jitter.
func (c *Client) backoff(attempt int) time.Duration {
	delay := c.opts.BaseDelay << (attempt - 2)
	return delay + time.Duration(rand.Int64N(int64(delay)/2+1))
}

func sleep(ctx context.Context, d time.Duration) error {
	timer := time.NewTimer(d)
	defer timer.Stop()
	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-timer.C:
		return nil
	}
}
