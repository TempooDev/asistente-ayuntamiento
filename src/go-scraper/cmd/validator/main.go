package main

import (
	"context"
	"fmt"
	"log"
	"os"

	"gocloud.dev/blob"
	_ "gocloud.dev/blob/azureblob"
)

func main() {
	ctx := context.Background()

	// Leemos la cadena de conexión del primer argumento si se provee
	connStr := ""
	if len(os.Args) > 1 {
		connStr = os.Args[1]
	}
	if connStr == "" {
		log.Fatalf("Error: debes proporcionar la cadena de conexión como argumento.\nEjemplo: go run ./cmd/validator \"DefaultEndpointsProtocol=http;AccountName=...\"")
	}

	// Inyectamos la cadena de conexión de Azurite (desarrollo)
	os.Setenv("AZURE_STORAGE_CONNECTION_STRING", connStr)
	
	bucket, err := blob.OpenBucket(ctx, "azblob://boletines")
	if err != nil {
		log.Fatalf("Error conectando a Azurite: %v\n(Asegúrate de que Azurite/Aspire está corriendo)", err)
	}
	defer bucket.Close()

	iter := bucket.List(nil)
	count := 0
	
	fmt.Println("Conexión exitosa a Azurite.")
	fmt.Println("Archivos guardados en el contenedor 'boletines':")
	fmt.Println("--------------------------------------------------")

	for {
		obj, err := iter.Next(ctx)
		if err != nil {
			if err.Error() == "EOF" {
				break
			}
			log.Fatalf("Error listando blobs: %v", err)
		}
		
		fmt.Printf("- %s (Tamaño: %d bytes)\n", obj.Key, obj.Size)
		count++
		
		// Mostramos todos, pero si quieres limitar, puedes poner un break
	}
	
	fmt.Println("--------------------------------------------------")
	if count == 0 {
		fmt.Println("No se encontraron archivos. El contenedor está vacío.")
	} else {
		fmt.Printf("\n¡Validación Completada! Total de archivos encontrados: %d\n", count)
	}
}
