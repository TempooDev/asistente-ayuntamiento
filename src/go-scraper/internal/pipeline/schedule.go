package pipeline

import (
	"context"
	"errors"
	"time"
)

// RunDaily scrapes the previous day for providers every day at hour (local time) until ctx is cancelled.
// A run that finds another job in progress is skipped, not queued.
func (o *Orchestrator) RunDaily(ctx context.Context, hour int, providers []string) {
	for {
		next := nextRun(o.cfg.Now(), hour)
		o.cfg.Logger.InfoContext(ctx, "next scheduled scrape", "at", next.Format(time.RFC3339), "providers", providers)

		timer := time.NewTimer(next.Sub(o.cfg.Now()))
		select {
		case <-ctx.Done():
			timer.Stop()
			return
		case <-timer.C:
		}

		yesterday := o.cfg.Now().AddDate(0, 0, -1)
		_, err := o.Run(ctx, "schedule", Request{Providers: append([]string(nil), providers...), Start: yesterday})
		switch {
		case errors.Is(err, ErrBusy):
			o.cfg.Logger.WarnContext(ctx, "scheduled scrape skipped: another job is running")
		case err != nil:
			o.cfg.Logger.ErrorContext(ctx, "scheduled scrape rejected", "error", err)
		}
	}
}

// nextRun is the next time at hour:00 strictly after now, in now's location.
func nextRun(now time.Time, hour int) time.Time {
	next := time.Date(now.Year(), now.Month(), now.Day(), hour, 0, 0, 0, now.Location())
	if !next.After(now) {
		next = next.AddDate(0, 0, 1)
	}
	return next
}
