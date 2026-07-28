package storage

import (
	"context"
	"encoding/json"
	"fmt"
	"strings"

	"github.com/Azure/azure-sdk-for-go/sdk/storage/azblob"
	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
)

// DocumentStorage define la interfaz para almacenar los resultados del scraper.
type DocumentStorage interface {
	SaveDocument(ctx context.Context, doc *scraper.Document) error
	SaveRawXML(ctx context.Context, source string, id string, data []byte) error
}

// AzureBlobStorage implementa DocumentStorage usando Azure Blob Storage (o Azurite).
type AzureBlobStorage struct {
	client        *azblob.Client
	containerName string
}

// NewAzureBlobStorage inicializa el cliente con la connection string y crea el contenedor si no existe.
func NewAzureBlobStorage(ctx context.Context, connStr, containerName string) (*AzureBlobStorage, error) {
	client, err := azblob.NewClientFromConnectionString(connStr, nil)
	if err != nil {
		return nil, fmt.Errorf("error creando cliente azure blob: %w", err)
	}

	// Aseguramos que el contenedor exista
	_, err = client.CreateContainer(ctx, containerName, nil)
	// Si el error indica que ya existe, lo ignoramos.
	if err != nil && !strings.Contains(err.Error(), "ContainerAlreadyExists") {
		return nil, fmt.Errorf("error creando contenedor %s: %w", containerName, err)
	}

	return &AzureBlobStorage{
		client:        client,
		containerName: containerName,
	}, nil
}

// SaveDocument convierte el Document a JSON y lo sube al blob storage.
func (s *AzureBlobStorage) SaveDocument(ctx context.Context, doc *scraper.Document) error {
	// Guardamos los JSON estructurados en la ruta json/Fuente/ID.json
	blobName := fmt.Sprintf("json/%s/%s.json", doc.Metadata.Source, doc.DocumentID)

	jsonData, err := json.MarshalIndent(doc, "", "  ")
	if err != nil {
		return fmt.Errorf("error serializando JSON para %s: %w", doc.DocumentID, err)
	}

	_, err = s.client.UploadBuffer(ctx, s.containerName, blobName, jsonData, &azblob.UploadBufferOptions{
		HTTPHeaders: &azblob.BlobHTTPHeaders{
			BlobContentType: toPtr("application/json"),
		},
	})
	if err != nil {
		return fmt.Errorf("error subiendo blob JSON %s: %w", blobName, err)
	}

	return nil
}

// SaveRawXML sube el XML original como mecanismo de backup para posibles reprocesados futuros.
func (s *AzureBlobStorage) SaveRawXML(ctx context.Context, source string, id string, data []byte) error {
	blobName := fmt.Sprintf("raw-xml/%s/%s.xml", source, id)

	_, err := s.client.UploadBuffer(ctx, s.containerName, blobName, data, &azblob.UploadBufferOptions{
		HTTPHeaders: &azblob.BlobHTTPHeaders{
			BlobContentType: toPtr("application/xml"),
		},
	})
	if err != nil {
		return fmt.Errorf("error subiendo raw XML %s: %w", blobName, err)
	}

	return nil
}

// toPtr es un helper para obtener el puntero de un string (requerido por azblob)
func toPtr(s string) *string {
	return &s
}
