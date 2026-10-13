package commandserver

import (
	"context"
	"io"
	"log/slog"
	"net"
	"testing"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/pipeline"
	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/credentials/insecure"
	"google.golang.org/grpc/metadata"
	"google.golang.org/grpc/status"
	"google.golang.org/grpc/test/bufconn"
)

type fakeRunner struct {
	job  pipeline.Job
	err  error
	last pipeline.Request
}

func (f *fakeRunner) Run(ctx context.Context, trigger string, req pipeline.Request) (pipeline.Job, error) {
	f.last = req
	return f.job, f.err
}

// dial starts the gRPC server in memory and returns a client.
func dial(t *testing.T, runner *fakeRunner, key string) pb.ScraperCommandServiceClient {
	t.Helper()
	listener := bufconn.Listen(1 << 20)
	server := NewGRPCServer(NewServer(runner, slog.New(slog.NewTextHandler(io.Discard, nil))), key)
	go server.Serve(listener)
	t.Cleanup(server.Stop)

	conn, err := grpc.NewClient("passthrough:///bufnet",
		grpc.WithContextDialer(func(ctx context.Context, _ string) (net.Conn, error) { return listener.DialContext(ctx) }),
		grpc.WithTransportCredentials(insecure.NewCredentials()))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { conn.Close() })
	return pb.NewScraperCommandServiceClient(conn)
}

func withKey(key string) context.Context {
	return metadata.AppendToOutgoingContext(context.Background(), InternalKeyHeader, key)
}

func TestCallsWithoutTheInternalKeyAreRejected(t *testing.T) {
	client := dial(t, &fakeRunner{}, "secret")

	for _, ctx := range []context.Context{context.Background(), withKey("wrong")} {
		_, err := client.ForceScrape(ctx, &pb.ForceScrapeRequest{Provider: "BOE"})
		if status.Code(err) != codes.Unauthenticated {
			t.Fatalf("err = %v, want Unauthenticated", err)
		}
	}
}

func TestForceScrapeRunsAJobWithTheRequestedRange(t *testing.T) {
	runner := &fakeRunner{job: pipeline.Job{Status: pipeline.StatusSucceeded, Saved: 12}}
	client := dial(t, runner, "secret")

	resp, err := client.ForceScrape(withKey("secret"), &pb.ForceScrapeRequest{Provider: "BOE", StartDate: "2026-10-01", EndDate: "2026-10-03", Sections: []string{"1"}})
	if err != nil {
		t.Fatal(err)
	}
	if !resp.Success || resp.ItemsExtracted != 12 {
		t.Fatalf("resp = %+v", resp)
	}
	if runner.last.Providers[0] != "BOE" || runner.last.Start.Format(time.DateOnly) != "2026-10-01" || runner.last.End.Format(time.DateOnly) != "2026-10-03" {
		t.Fatalf("request = %+v", runner.last)
	}
}

func TestBusyInvalidAndFailedJobsAreUnsuccessfulResponses(t *testing.T) {
	cases := []*fakeRunner{
		{err: pipeline.ErrBusy},
		{err: &pipeline.ValidationError{Reason: "the end date is in the future"}},
		{job: pipeline.Job{Status: pipeline.StatusFailed, Error: "loading filter rules"}},
	}
	for _, runner := range cases {
		resp, err := dial(t, runner, "").ForceScrape(context.Background(), &pb.ForceScrapeRequest{Provider: "BOE"})
		if err != nil || resp.Success || resp.Message == "" {
			t.Fatalf("resp = %+v err = %v", resp, err)
		}
	}

	resp, _ := dial(t, &fakeRunner{}, "").ForceScrape(context.Background(), &pb.ForceScrapeRequest{StartDate: "01/10/2026"})
	if resp.Success {
		t.Fatal("an invalid date must not succeed")
	}
}
