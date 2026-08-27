package commandserver

import (
	"context"
	"fmt"
	"log"
	"net"
	"os"

	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
	"google.golang.org/grpc"
)

type Server struct {
	pb.UnimplementedScraperCommandServiceServer
	onForceScrape func(provider string) (int, error)
}

func NewServer(onForceScrape func(provider string) (int, error)) *Server {
	return &Server{
		onForceScrape: onForceScrape,
	}
}

func (s *Server) ForceScrape(ctx context.Context, req *pb.ForceScrapeRequest) (*pb.ForceScrapeResponse, error) {
	log.Printf("Received ForceScrape command for provider: %s", req.Provider)
	
	items, err := s.onForceScrape(req.Provider)
	if err != nil {
		return &pb.ForceScrapeResponse{
			Success: false,
			Message: err.Error(),
			ItemsExtracted: 0,
		}, nil
	}

	return &pb.ForceScrapeResponse{
		Success: true,
		Message: "Scrape successful",
		ItemsExtracted: int32(items),
	}, nil
}

func StartGrpcServer(onForceScrape func(provider string) (int, error)) {
	port := os.Getenv("GRPC_PORT")
	if port == "" {
		port = "50051"
	}

	lis, err := net.Listen("tcp", fmt.Sprintf(":%s", port))
	if err != nil {
		log.Fatalf("failed to listen: %v", err)
	}

	grpcServer := grpc.NewServer()
	pb.RegisterScraperCommandServiceServer(grpcServer, NewServer(onForceScrape))

	log.Printf("gRPC server listening at %v", lis.Addr())
	if err := grpcServer.Serve(lis); err != nil {
		log.Fatalf("failed to serve gRPC: %v", err)
	}
}
