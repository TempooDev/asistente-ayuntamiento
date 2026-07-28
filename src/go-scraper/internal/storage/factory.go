package storage

import (
	"context"
	"fmt"
	"os"
)

// NewDocumentStorage crea la implementación adecuada (Azure o S3/R2) según las variables de entorno inyectadas por Aspire.
func NewDocumentStorage(ctx context.Context) (DocumentStorage, error) {
	// 1. Verificar si hay configuración S3/R2 inyectada (Cloudflare R2 / AWS)
	endpoint := os.Getenv("Blob__Endpoint")
	if endpoint != "" {
		accessKey := os.Getenv("Blob__AccessKeyId")
		secretKey := os.Getenv("Blob__SecretAccessKey")
		bucketName := os.Getenv("Blob__BucketName")
		if bucketName == "" {
			bucketName = "boletines" // default
		}
		return NewS3BlobStorage(ctx, endpoint, accessKey, secretKey, bucketName)
	}

	// 2. Si no hay S3/R2, intentar Azure (usado localmente con Azurite)
	connStr := os.Getenv("ConnectionStrings__BlobStorage")
	if connStr != "" {
		return NewAzureBlobStorage(ctx, connStr, "boletines")
	}

	return nil, fmt.Errorf("no se encontró configuración para Blob Storage (ni R2 ni Azure)")
}
