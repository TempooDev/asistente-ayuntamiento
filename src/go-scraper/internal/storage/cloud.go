// Package storage writes scraped documents to the S3-compatible bucket shared with the .NET ingestion.
package storage

import (
	"context"
	"encoding/json"
	"fmt"

	"github.com/asistente-ayuntamiento/go-scraper/internal/config"
	"github.com/asistente-ayuntamiento/go-scraper/internal/provider"
	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
	"github.com/aws/aws-sdk-go-v2/aws"
	"github.com/aws/aws-sdk-go-v2/credentials"
	"github.com/aws/aws-sdk-go-v2/service/s3"
	"gocloud.dev/blob"
	"gocloud.dev/blob/s3blob"
)

// DocumentStorage stores the parsed document (read by the .NET ingestion) and the original as a backup.
type DocumentStorage interface {
	SaveDocument(ctx context.Context, doc *scraper.Document) error
	SaveRaw(ctx context.Context, source, id string, raw provider.Raw) error
	Close() error
}

// DocumentPath is the blob key of a parsed document; the ingestion messages carry it.
func DocumentPath(source, id string) string {
	return fmt.Sprintf("json/%s/%s.json", source, id)
}

// RawPath is the blob key of the original download: raw-xml/BOE/<id>.xml, raw-html/BOJA/<id>.html.
// The BOE hierarchical ingestion reads raw-xml/BOE/<id>.xml.
func RawPath(source, id, extension string) string {
	return fmt.Sprintf("raw-%s/%s/%s.%s", extension, source, id, extension)
}

type CloudStorage struct {
	bucket *blob.Bucket
}

// NewCloudStorage opens the bucket with explicit credentials (no process-wide AWS_* variables).
// Path-style addressing and the "auto" region are what MinIO and Cloudflare R2 expect.
func NewCloudStorage(ctx context.Context, cfg config.BlobConfig) (*CloudStorage, error) {
	client := s3.NewFromConfig(aws.Config{
		Region:      "auto",
		Credentials: credentials.NewStaticCredentialsProvider(cfg.AccessKeyID, cfg.SecretAccessKey, ""),
	}, func(o *s3.Options) {
		o.BaseEndpoint = aws.String(cfg.Endpoint)
		o.UsePathStyle = true
	})

	bucket, err := s3blob.OpenBucket(ctx, client, cfg.Bucket, nil)
	if err != nil {
		return nil, fmt.Errorf("opening bucket %s at %s: %w", cfg.Bucket, cfg.Endpoint, err)
	}
	return &CloudStorage{bucket: bucket}, nil
}

func (s *CloudStorage) SaveDocument(ctx context.Context, doc *scraper.Document) error {
	key := DocumentPath(doc.Metadata.Source, doc.DocumentID)
	data, err := json.MarshalIndent(doc, "", "  ")
	if err != nil {
		return fmt.Errorf("serializing %s: %w", doc.DocumentID, err)
	}
	if err := s.bucket.WriteAll(ctx, key, data, &blob.WriterOptions{ContentType: "application/json"}); err != nil {
		return fmt.Errorf("writing %s: %w", key, err)
	}
	return nil
}

func (s *CloudStorage) SaveRaw(ctx context.Context, source, id string, raw provider.Raw) error {
	key := RawPath(source, id, raw.Extension)
	if err := s.bucket.WriteAll(ctx, key, raw.Data, &blob.WriterOptions{ContentType: raw.ContentType}); err != nil {
		return fmt.Errorf("writing %s: %w", key, err)
	}
	return nil
}

func (s *CloudStorage) Close() error {
	return s.bucket.Close()
}
