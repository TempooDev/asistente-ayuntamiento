package storage

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"

	"github.com/aws/aws-sdk-go-v2/aws"
	"github.com/aws/aws-sdk-go-v2/config"
	"github.com/aws/aws-sdk-go-v2/credentials"
	"github.com/aws/aws-sdk-go-v2/service/s3"
	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
)

type S3BlobStorage struct {
	client     *s3.Client
	bucketName string
}

func NewS3BlobStorage(ctx context.Context, endpoint, accessKey, secretKey, bucketName string) (*S3BlobStorage, error) {
	customResolver := aws.EndpointResolverWithOptionsFunc(func(service, region string, options ...interface{}) (aws.Endpoint, error) {
		return aws.Endpoint{
			URL:               endpoint,
			HostnameImmutable: true,
		}, nil
	})

	cfg, err := config.LoadDefaultConfig(ctx,
		config.WithEndpointResolverWithOptions(customResolver),
		config.WithCredentialsProvider(credentials.NewStaticCredentialsProvider(accessKey, secretKey, "")),
		config.WithRegion("auto"), // R2 suele ignorar la región, pero el SDK necesita un valor
	)
	if err != nil {
		return nil, fmt.Errorf("error cargando config S3/R2: %w", err)
	}

	client := s3.NewFromConfig(cfg)

	return &S3BlobStorage{
		client:     client,
		bucketName: bucketName,
	}, nil
}

func (s *S3BlobStorage) SaveDocument(ctx context.Context, doc *scraper.Document) error {
	blobName := fmt.Sprintf("json/%s/%s.json", doc.Metadata.Source, doc.DocumentID)
	jsonData, err := json.MarshalIndent(doc, "", "  ")
	if err != nil {
		return fmt.Errorf("error serializando JSON para %s: %w", doc.DocumentID, err)
	}

	_, err = s.client.PutObject(ctx, &s3.PutObjectInput{
		Bucket:      aws.String(s.bucketName),
		Key:         aws.String(blobName),
		Body:        bytes.NewReader(jsonData),
		ContentType: aws.String("application/json"),
	})
	if err != nil {
		return fmt.Errorf("error subiendo blob JSON a S3 %s: %w", blobName, err)
	}
	return nil
}

func (s *S3BlobStorage) SaveRawXML(ctx context.Context, source string, id string, data []byte) error {
	blobName := fmt.Sprintf("raw-xml/%s/%s.xml", source, id)
	_, err := s.client.PutObject(ctx, &s3.PutObjectInput{
		Bucket:      aws.String(s.bucketName),
		Key:         aws.String(blobName),
		Body:        bytes.NewReader(data),
		ContentType: aws.String("application/xml"),
	})
	if err != nil {
		return fmt.Errorf("error subiendo raw XML a S3 %s: %w", blobName, err)
	}
	return nil
}
