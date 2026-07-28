package main

import (
	"context"
	"fmt"
	"log"
	"net/http"
	"os"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/boe"
	"github.com/asistente-ayuntamiento/go-scraper/internal/storage"
)

func main() {
	fmt.Println("Iniciando BOE Scraper...")

	// Endpoint health check para validación en .NET Aspire
	http.HandleFunc("/health", func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
		w.Write([]byte("BOE Scraper is running\n"))
	})

	// Ejecutar el scraping en una goroutine para no bloquear el health check
	go runScraperWorkflow()

	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}
	fmt.Printf("Servidor escuchando en puerto %s\n", port)
	if err := http.ListenAndServe(":"+port, nil); err != nil {
		log.Fatalf("Error al iniciar el servidor: %v", err)
	}
}

func runScraperWorkflow() {
	ctx := context.Background()

	connStr := os.Getenv("ConnectionStrings__BlobStorage")
	if connStr == "" {
		log.Println("WARNING: ConnectionStrings__BlobStorage no está definida. Saltando el scraping por ahora.")
		return
	}

	blobStorage, err := storage.NewAzureBlobStorage(ctx, connStr, "boletines")
	if err != nil {
		log.Printf("Error inicializando storage: %v\n", err)
		return
	}

	boeProvider := boe.NewProvider()

	// Obtenemos los documentos de hoy (o de una fecha específica de prueba)
	today := time.Now()
	log.Printf("Obteniendo sumario BOE para %s...\n", today.Format("2006-01-02"))

	ids, err := boeProvider.FetchSummary(ctx, today)
	if err != nil {
		log.Printf("Error obteniendo sumario: %v\n", err)
		return
	}

	log.Printf("Encontrados %d documentos en el sumario.\n", len(ids))

	// Procesar los primeros 3 a modo de prueba en esta fase temprana.
	limit := len(ids)
	if limit > 3 {
		limit = 3
	}

	for _, id := range ids[:limit] {
		log.Printf("Procesando documento %s...\n", id)
		doc, err := boeProvider.FetchDocument(ctx, id)
		if err != nil {
			log.Printf("Error al procesar %s: %v\n", id, err)
			continue
		}

		if err := blobStorage.SaveDocument(ctx, doc); err != nil {
			log.Printf("Error guardando JSON para %s: %v\n", id, err)
		} else {
			log.Printf("Documento %s guardado con éxito.\n", id)
		}
	}

	log.Println("Scraping diario completado.")
}
