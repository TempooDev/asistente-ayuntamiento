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
)

type Client struct {
	conn *grpc.ClientConn
	svc  pb.FilterConfigServiceClient
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

	return &Client{
		conn: conn,
		svc:  pb.NewFilterConfigServiceClient(conn),
	}, nil
}

func (c *Client) Close() {
	if c.conn != nil {
		c.conn.Close()
	}
}

func (c *Client) GetFilters(ctx context.Context) ([]*pb.FilterRule, error) {
	resp, err := c.svc.GetActiveFilters(ctx, &pb.EmptyRequest{})
	if err != nil {
		log.Printf("Error fetching filters from .NET API: %v", err)
		return nil, err
	}
	return resp.Rules, nil
}
