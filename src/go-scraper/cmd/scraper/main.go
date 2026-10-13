// Command scraper downloads official bulletins (BOE, BOJA), stores them in the shared bucket and queues them
// for the .NET ingestion. It serves an HTTP API, a gRPC command service and a daily schedule.
package main

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/commandserver"
	"github.com/asistente-ayuntamiento/go-scraper/internal/config"
	"github.com/asistente-ayuntamiento/go-scraper/internal/filterclient"
	"github.com/asistente-ayuntamiento/go-scraper/internal/httpapi"
	"github.com/asistente-ayuntamiento/go-scraper/internal/logging"
	"github.com/asistente-ayuntamiento/go-scraper/internal/messaging"
	"github.com/asistente-ayuntamiento/go-scraper/internal/pipeline"
	"github.com/asistente-ayuntamiento/go-scraper/internal/provider"
	"github.com/asistente-ayuntamiento/go-scraper/internal/provider/boe"
	"github.com/asistente-ayuntamiento/go-scraper/internal/provider/boja"
	"github.com/asistente-ayuntamiento/go-scraper/internal/storage"
	"github.com/asistente-ayuntamiento/go-scraper/internal/telemetry"
)

// shutdownTimeout bounds the graceful stop of servers, jobs and telemetry.
const shutdownTimeout = 30 * time.Second

func main() {
	if err := run(); err != nil {
		slog.Error("scraper stopped with an error", "error", err)
		os.Exit(1)
	}
}

func run() error {
	cfg, err := config.Load(os.Getenv)
	logger := logging.New(os.Stdout, cfg.LogLevel)
	slog.SetDefault(logger)
	if err != nil {
		return fmt.Errorf("invalid configuration: %w", err)
	}

	// Cancelled by SIGINT/SIGTERM: stops the schedule and the running job.
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	shutdownTelemetry, err := telemetry.InitProvider(ctx)
	if err != nil {
		logger.Warn("OpenTelemetry disabled", "error", err)
		shutdownTelemetry = func(context.Context) error { return nil }
	}
	defer func() {
		flushCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		if err := shutdownTelemetry(flushCtx); err != nil {
			logger.Warn("telemetry flush failed", "error", err)
		}
	}()

	store, err := storage.NewCloudStorage(ctx, cfg.Blob)
	if err != nil {
		return err
	}
	defer store.Close()

	var publisher pipeline.Publisher
	if cfg.RabbitMQURL == "" {
		logger.Warn("ConnectionStrings__messaging is not set: documents are stored but not queued for ingestion")
	} else {
		p := messaging.NewPublisher(cfg.RabbitMQURL, logger)
		defer p.Close()
		publisher = p
	}

	filters, err := filterclient.NewClient(cfg.FilterAPITarget, cfg.InternalKey)
	if err != nil {
		return fmt.Errorf("filter service client: %w", err)
	}
	defer filters.Close()

	// Only BOE and BOJA are in the project's scope.
	orchestrator := pipeline.New(pipeline.Config{
		Providers: []provider.Provider{
			boe.NewProvider(boe.NewHTTPClient(), ""),
			boja.NewProvider(boja.NewHTTPClient(), "", cfg.BojaFeeds),
		},
		Storage:      store,
		Publisher:    publisher,
		Filters:      filters,
		MaxRangeDays: cfg.MaxRangeDays,
		Logger:       logger,
	})

	httpServer := &http.Server{
		Addr:              ":" + cfg.HTTPPort,
		Handler:           httpapi.NewHandler(ctx, orchestrator),
		ReadHeaderTimeout: 10 * time.Second,
		ReadTimeout:       30 * time.Second,
		WriteTimeout:      30 * time.Second,
		IdleTimeout:       2 * time.Minute,
	}
	grpcListener, err := net.Listen("tcp", ":"+cfg.GRPCPort)
	if err != nil {
		return fmt.Errorf("gRPC listener: %w", err)
	}
	grpcServer := commandserver.NewGRPCServer(commandserver.NewServer(orchestrator, logger), cfg.InternalKey)

	serveErrors := make(chan error, 2)
	go func() {
		logger.Info("HTTP server listening", "port", cfg.HTTPPort)
		if err := httpServer.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			serveErrors <- fmt.Errorf("HTTP server: %w", err)
		}
	}()
	go func() {
		logger.Info("gRPC server listening", "port", cfg.GRPCPort)
		if err := grpcServer.Serve(grpcListener); err != nil {
			serveErrors <- fmt.Errorf("gRPC server: %w", err)
		}
	}()
	go orchestrator.RunDaily(ctx, cfg.ScheduleHour, cfg.ScheduledProviders)

	var serveErr error
	select {
	case <-ctx.Done():
		logger.Info("shutdown requested")
	case serveErr = <-serveErrors:
		stop()
	}

	shutdownCtx, cancel := context.WithTimeout(context.Background(), shutdownTimeout)
	defer cancel()
	if err := httpServer.Shutdown(shutdownCtx); err != nil {
		logger.Warn("HTTP shutdown", "error", err)
	}
	stopGRPC(shutdownCtx, grpcServer)
	waitForJobs(shutdownCtx, orchestrator, logger)
	logger.Info("scraper stopped")
	return serveErr
}

// stopGRPC lets in-flight calls finish, but not beyond ctx.
func stopGRPC(ctx context.Context, server interface {
	GracefulStop()
	Stop()
}) {
	done := make(chan struct{})
	go func() {
		server.GracefulStop()
		close(done)
	}()
	select {
	case <-done:
	case <-ctx.Done():
		server.Stop()
	}
}

// waitForJobs waits for background jobs, which see the cancelled context and stop between documents.
func waitForJobs(ctx context.Context, orchestrator *pipeline.Orchestrator, logger *slog.Logger) {
	done := make(chan struct{})
	go func() {
		orchestrator.Wait()
		close(done)
	}()
	select {
	case <-done:
	case <-ctx.Done():
		logger.Warn("background scrape job did not stop in time")
	}
}
