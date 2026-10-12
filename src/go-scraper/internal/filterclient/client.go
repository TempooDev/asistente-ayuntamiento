package filterclient

import (
	"context"
	"crypto/tls"
	"log"
	"os"
	"strings"

	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
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

func NewClient() (*Client, error) {
	target := os.Getenv("DOTNET_API_GRPC_URL")
	if target == "" {
		target = "localhost:5001" // fallback for local dev
	}

	useTLS := strings.HasPrefix(target, "https://")

	// grpc.Dial expects host:port without the scheme
	target = strings.TrimPrefix(target, "http://")
	target = strings.TrimPrefix(target, "https://")

	var creds credentials.TransportCredentials
	if useTLS {
		creds = credentials.NewTLS(&tls.Config{InsecureSkipVerify: true})
	} else {
		creds = insecure.NewCredentials()
	}

	conn, err := grpc.Dial(target, grpc.WithTransportCredentials(creds))
	if err != nil {
		return nil, err
	}

	internalKey := os.Getenv("INTERNAL_GRPC_KEY")
	if internalKey == "" {
		log.Printf("Aviso: INTERNAL_GRPC_KEY no configurada; la API rechazará las llamadas fuera de Development")
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
		log.Printf("Error fetching filters from .NET API: %v", err)
		return nil, err
	}
	return resp.Rules, nil
}
