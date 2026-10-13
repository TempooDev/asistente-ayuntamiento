// Package protos holds the gRPC code generated from src/Protos/scraper.proto (shared with the .NET API).
// Regenerate with protoc 33.2, protoc-gen-go v1.36.12 and protoc-gen-go-grpc v1.6.2 (the versions CI checks):
//
//	go generate ./internal/protos
package protos

//go:generate protoc -I ../../../Protos --go_out=../.. --go_opt=module=github.com/asistente-ayuntamiento/go-scraper --go-grpc_out=../.. --go-grpc_opt=module=github.com/asistente-ayuntamiento/go-scraper scraper.proto
