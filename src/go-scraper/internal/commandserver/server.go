// Package commandserver exposes the scraper commands over gRPC to the .NET API.
package commandserver

import (
	"context"
	"crypto/subtle"
	"errors"
	"fmt"
	"log/slog"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/pipeline"
	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
	"go.opentelemetry.io/contrib/instrumentation/google.golang.org/grpc/otelgrpc"
	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/metadata"
	"google.golang.org/grpc/status"
)

// InternalKeyHeader must match InternalApiKeyInterceptor.HeaderName in the .NET API.
const InternalKeyHeader = "x-internal-key"

// Runner is the part of the orchestrator the command server needs.
type Runner interface {
	Run(ctx context.Context, trigger string, req pipeline.Request) (pipeline.Job, error)
}

type Server struct {
	pb.UnimplementedScraperCommandServiceServer
	runner Runner
	logger *slog.Logger
	now    func() time.Time
}

func NewServer(runner Runner, logger *slog.Logger) *Server {
	return &Server{runner: runner, logger: logger, now: time.Now}
}

// ForceScrape runs a job synchronously. Dates are YYYY-MM-DD; an empty start means today and an empty end
// means the start date. Business failures (busy, invalid request, failed job) come back as Success=false.
func (s *Server) ForceScrape(ctx context.Context, req *pb.ForceScrapeRequest) (*pb.ForceScrapeResponse, error) {
	start, end := s.now(), time.Time{}
	var err error
	if req.StartDate != "" {
		if start, err = time.Parse(time.DateOnly, req.StartDate); err != nil {
			return failure(fmt.Sprintf("invalid start date %q", req.StartDate)), nil
		}
	}
	if req.EndDate != "" {
		if end, err = time.Parse(time.DateOnly, req.EndDate); err != nil {
			return failure(fmt.Sprintf("invalid end date %q", req.EndDate)), nil
		}
	}

	var providers []string
	if req.Provider != "" {
		providers = []string{req.Provider}
	}
	job, err := s.runner.Run(ctx, "grpc", pipeline.Request{Providers: providers, Start: start, End: end, Sections: req.Sections})
	switch {
	case errors.Is(err, pipeline.ErrBusy):
		return failure("Ya hay un proceso de scraping en curso"), nil
	case errors.Is(err, pipeline.ErrStopping):
		return failure("El scraper se está deteniendo; inténtalo de nuevo en unos minutos"), nil
	case err != nil:
		return failure(err.Error()), nil
	case job.Status == pipeline.StatusFailed:
		return &pb.ForceScrapeResponse{Success: false, Message: job.Error, ItemsExtracted: int32(job.Saved)}, nil
	}
	return &pb.ForceScrapeResponse{Success: true, Message: "Scrape successful", ItemsExtracted: int32(job.Saved)}, nil
}

func failure(message string) *pb.ForceScrapeResponse {
	return &pb.ForceScrapeResponse{Success: false, Message: message}
}

// NewGRPCServer registers the command service with tracing and requires callers to send internalKey in the
// x-internal-key metadata. An empty key is a programming error (config.Load rejects it).
func NewGRPCServer(server *Server, internalKey string) *grpc.Server {
	if internalKey == "" {
		panic("commandserver: internal key is required")
	}
	grpcServer := grpc.NewServer(
		grpc.StatsHandler(otelgrpc.NewServerHandler()),
		grpc.UnaryInterceptor(requireInternalKey(internalKey)),
	)
	pb.RegisterScraperCommandServiceServer(grpcServer, server)
	return grpcServer
}

func requireInternalKey(expected string) grpc.UnaryServerInterceptor {
	return func(ctx context.Context, req any, info *grpc.UnaryServerInfo, handler grpc.UnaryHandler) (any, error) {
		md, _ := metadata.FromIncomingContext(ctx)
		values := md.Get(InternalKeyHeader)
		if len(values) != 1 || subtle.ConstantTimeCompare([]byte(values[0]), []byte(expected)) != 1 {
			return nil, status.Error(codes.Unauthenticated, "missing or invalid internal key")
		}
		return handler(ctx, req)
	}
}
