// Package config loads and validates the scraper configuration from environment variables.
package config

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
)

// Config is the complete runtime configuration. Load it once in main and pass the parts down.
type Config struct {
	HTTPPort string
	GRPCPort string

	// RabbitMQURL is optional: without it documents are stored but not queued for ingestion.
	RabbitMQURL string

	Blob BlobConfig

	// FilterAPITarget is the .NET gRPC endpoint that serves the filter rules (http:// or https://host:port).
	FilterAPITarget string
	// InternalKey authenticates gRPC calls between the API and the scraper in both directions.
	InternalKey string

	// BojaFeeds overrides the default BOJA Atom feeds (filter rules of type BojaFeed take precedence).
	BojaFeeds []string

	// ScheduleHour is the local hour of the daily scrape of the previous day (0-23).
	ScheduleHour int
	// ScheduledProviders are scraped by the daily schedule.
	ScheduledProviders []string
	// MaxRangeDays bounds manual scrapes (inclusive range length).
	MaxRangeDays int

	LogLevel string
}

// BlobConfig points to the S3-compatible bucket (MinIO, Cloudflare R2).
type BlobConfig struct {
	Endpoint        string
	Bucket          string
	AccessKeyID     string
	SecretAccessKey string
}

const DefaultBucket = "boletines"

// MinInternalKeyLength is the shortest accepted INTERNAL_GRPC_KEY (AppHost generates 32 characters).
const MinInternalKeyLength = 16

// Load reads the configuration through getenv (os.Getenv in production) and returns every problem at once.
func Load(getenv func(string) string) (Config, error) {
	var problems []error
	get := func(key, fallback string) string {
		if v := strings.TrimSpace(getenv(key)); v != "" {
			return v
		}
		return fallback
	}
	intValue := func(key string, fallback, min, max int) int {
		raw := get(key, "")
		if raw == "" {
			return fallback
		}
		v, err := strconv.Atoi(raw)
		if err != nil || v < min || v > max {
			problems = append(problems, fmt.Errorf("%s must be an integer between %d and %d (got %q)", key, min, max, raw))
			return fallback
		}
		return v
	}
	port := func(key, fallback string) string {
		v := get(key, fallback)
		if n, err := strconv.Atoi(v); err != nil || n < 1 || n > 65535 {
			problems = append(problems, fmt.Errorf("%s must be a TCP port (got %q)", key, v))
		}
		return v
	}

	cfg := Config{
		HTTPPort:           port("PORT", "8080"),
		GRPCPort:           port("GRPC_PORT", "50051"),
		RabbitMQURL:        get("ConnectionStrings__messaging", ""),
		FilterAPITarget:    get("DOTNET_API_GRPC_URL", "http://localhost:5001"),
		InternalKey:        get("INTERNAL_GRPC_KEY", ""),
		BojaFeeds:          list(get("BOJA_FEEDS", "")),
		ScheduleHour:       intValue("SCRAPE_SCHEDULE_HOUR", 1, 0, 23),
		ScheduledProviders: list(get("SCRAPE_SCHEDULED_PROVIDERS", "BOE")),
		MaxRangeDays:       intValue("SCRAPE_MAX_RANGE_DAYS", 31, 1, 366),
		LogLevel:           get("LOG_LEVEL", "info"),
		Blob: BlobConfig{
			Endpoint:        get("Blob__Endpoint", ""),
			Bucket:          get("Blob__BucketName", DefaultBucket),
			AccessKeyID:     get("Blob__AccessKeyId", ""),
			SecretAccessKey: get("Blob__SecretAccessKey", ""),
		},
	}

	// AppHost generates the key and compose requires it: without it anyone on the network could start scrapes.
	if len(cfg.InternalKey) < MinInternalKeyLength {
		problems = append(problems, fmt.Errorf("INTERNAL_GRPC_KEY is required (at least %d characters)", MinInternalKeyLength))
	}
	if cfg.Blob.Endpoint == "" {
		problems = append(problems, errors.New("Blob__Endpoint is required"))
	} else if cfg.Blob.AccessKeyID == "" || cfg.Blob.SecretAccessKey == "" {
		problems = append(problems, errors.New("Blob__AccessKeyId and Blob__SecretAccessKey are required with Blob__Endpoint"))
	}
	if !strings.HasPrefix(cfg.FilterAPITarget, "http://") && !strings.HasPrefix(cfg.FilterAPITarget, "https://") {
		problems = append(problems, fmt.Errorf("DOTNET_API_GRPC_URL must start with http:// or https:// (got %q)", cfg.FilterAPITarget))
	}
	switch strings.ToLower(cfg.LogLevel) {
	case "debug", "info", "warn", "error":
	default:
		problems = append(problems, fmt.Errorf("LOG_LEVEL must be debug, info, warn or error (got %q)", cfg.LogLevel))
	}

	return cfg, errors.Join(problems...)
}

// list splits a comma-separated value, dropping blanks.
func list(value string) []string {
	var out []string
	for _, item := range strings.Split(value, ",") {
		if item = strings.TrimSpace(item); item != "" {
			out = append(out, item)
		}
	}
	return out
}
