// Package filterclient reads the scraper filter rules from the .NET API over gRPC.
package filterclient

import (
	"context"
	"crypto/tls"
	"fmt"
	"strings"

	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
	"go.opentelemetry.io/contrib/instrumentation/google.golang.org/grpc/otelgrpc"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
	"google.golang.org/grpc/credentials/insecure"
	"google.golang.org/grpc/metadata"
)

// internalKeyHeader must match InternalApiKeyInterceptor.HeaderName in the .NET API.
const internalKeyHeader = "x-internal-key"

type Client struct {
	conn        *grpc.ClientConn
	svc         pb.FilterConfigServiceClient
	internalKey string
}

// NewClient connects to target (http://host:port or https://host:port). internalKey authenticates the calls;
// the API rejects calls without it outside Development.
func NewClient(target, internalKey string) (*Client, error) {
	useTLS := strings.HasPrefix(target, "https://")

	// The gRPC target is host:port without the scheme
	target = strings.TrimPrefix(target, "http://")
	target = strings.TrimPrefix(target, "https://")

	var creds credentials.TransportCredentials
	if useTLS {
		// Certificates are verified against the system trust store.
		creds = credentials.NewTLS(&tls.Config{MinVersion: tls.VersionTLS12})
	} else {
		creds = insecure.NewCredentials()
	}

	conn, err := grpc.NewClient(target, grpc.WithTransportCredentials(creds), grpc.WithStatsHandler(otelgrpc.NewClientHandler()))
	if err != nil {
		return nil, err
	}

	return &Client{
		conn:        conn,
		svc:         pb.NewFilterConfigServiceClient(conn),
		internalKey: internalKey,
	}, nil
}

func (c *Client) Close() {
	if c.conn != nil {
		c.conn.Close()
	}
}

func (c *Client) GetFilters(ctx context.Context) ([]*pb.FilterRule, error) {
	if c.internalKey != "" {
		ctx = metadata.AppendToOutgoingContext(ctx, internalKeyHeader, c.internalKey)
	}
	resp, err := c.svc.GetActiveFilters(ctx, &pb.EmptyRequest{})
	if err != nil {
		return nil, fmt.Errorf("fetching filter rules from the API: %w", err)
	}
	return resp.Rules, nil
}
