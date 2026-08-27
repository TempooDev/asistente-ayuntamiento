package filterclient

import (
	"context"
	"log"
	"os"

	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials/insecure"
)

type Client struct {
	conn *grpc.ClientConn
	svc  pb.FilterConfigServiceClient
}

func NewClient() (*Client, error) {
	// The .NET API host
	target := os.Getenv("DOTNET_API_GRPC_URL")
	if target == "" {
		target = "localhost:5001" // fallback for local dev
	}

	conn, err := grpc.Dial(target, grpc.WithTransportCredentials(insecure.NewCredentials()))
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
