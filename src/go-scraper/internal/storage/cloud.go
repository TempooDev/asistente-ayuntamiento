package storage

import (
	"context"
	"encoding/json"
	"fmt"
	"os"

	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"

	"gocloud.dev/blob"
	_ "gocloud.dev/blob/azureblob"
	_ "gocloud.dev/blob/s3blob"
)

// DocumentStorage define la interfaz agnóstica para almacenar los resultados del scraper.
type DocumentStorage interface {
	SaveDocument(ctx context.Context, doc *scraper.Document) error
	SaveRawXML(ctx context.Context, source string, id string, data []byte) error
	Close() error
}

// CloudStorage implementa DocumentStorage usando el Go Cloud Development Kit (gocloud.dev/blob).
type CloudStorage struct {
	bucket *blob.Bucket
}

// NewDocumentStorage crea el bucket agnóstico (Azure o S3/R2) según las variables de entorno inyectadas por Aspire.
func NewDocumentStorage(ctx context.Context) (DocumentStorage, error) {
	var bucketURL string

	// 1. Verificar si hay configuración S3/R2 inyectada (Cloudflare R2 / AWS)
	endpoint := os.Getenv("Blob__Endpoint")
	if endpoint != "" {
		bucketName := os.Getenv("Blob__BucketName")
		if bucketName == "" {
			bucketName = "boletines" // fallback por defecto
		}

		// S3 driver de gocloud espera que las credenciales estén en el entorno de AWS
		os.Setenv("AWS_ACCESS_KEY_ID", os.Getenv("Blob__AccessKeyId"))
		os.Setenv("AWS_SECRET_ACCESS_KEY", os.Getenv("Blob__SecretAccessKey"))
		
		// Indicamos region dummy porque R2 la ignora pero el driver la necesita, 
		// y usamos s3ForcePathStyle=true que es requerido por S3 compatibles como R2 y MinIO.
		bucketURL = fmt.Sprintf("s3://%s?endpoint=%s&region=auto&s3ForcePathStyle=true", bucketName, endpoint)
	} else {
		// 2. Si no hay S3/R2, intentar usar Azure Blob Storage (Azurite en local)
		connStr := os.Getenv("ConnectionStrings__BlobStorage")
		if connStr != "" {
			// El driver azureblob busca esta variable de entorno de conexión
			os.Setenv("AZURE_STORAGE_CONNECTION_STRING", connStr)
			// En Azurite el nombre del bucket puede requerir precreación por parte del AppHost
			bucketURL = "azblob://boletines"
		} else {
			return nil, fmt.Errorf("no se encontró configuración para Blob Storage (ni R2 ni Azure)")
		}
	}

	bucket, err := blob.OpenBucket(ctx, bucketURL)
	if err != nil {
		return nil, fmt.Errorf("error abriendo bucket %s: %w", bucketURL, err)
	}

	return &CloudStorage{bucket: bucket}, nil
}

// SaveDocument convierte el Document a JSON y lo sube al bucket configurado.
func (s *CloudStorage) SaveDocument(ctx context.Context, doc *scraper.Document) error {
	blobName := fmt.Sprintf("json/%s/%s.json", doc.Metadata.Source, doc.DocumentID)
	jsonData, err := json.MarshalIndent(doc, "", "  ")
	if err != nil {
		return fmt.Errorf("error serializando JSON para %s: %w", doc.DocumentID, err)
	}

	opts := &blob.WriterOptions{ContentType: "application/json"}
	err = s.bucket.WriteAll(ctx, blobName, jsonData, opts)
	if err != nil {
		return fmt.Errorf("error subiendo blob JSON %s: %w", blobName, err)
	}

	return nil
}

// SaveRawXML sube el XML original como mecanismo de backup agnóstico.
func (s *CloudStorage) SaveRawXML(ctx context.Context, source string, id string, data []byte) error {
	blobName := fmt.Sprintf("raw-xml/%s/%s.xml", source, id)
	
	opts := &blob.WriterOptions{ContentType: "application/xml"}
	err := s.bucket.WriteAll(ctx, blobName, data, opts)
	if err != nil {
		return fmt.Errorf("error subiendo raw XML %s: %w", blobName, err)
	}

	return nil
}

// Close libera los recursos del bucket.
func (s *CloudStorage) Close() error {
	return s.bucket.Close()
}
