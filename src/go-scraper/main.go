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
	"github.com/asistente-ayuntamiento/go-scraper/internal/telemetry"
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

	shutdown, err := telemetry.InitProvider(ctx)
	if err != nil {
		log.Printf("Error inicializando OpenTelemetry: %v\n", err)
	} else {
		defer shutdown(ctx)
	}

	blobStorage, err := storage.NewDocumentStorage(ctx)
	if err != nil {
		log.Printf("Error inicializando storage: %v\n", err)
		return
	}
	defer blobStorage.Close()

	// Lista de fuentes (boletines) a procesar
	providers := []scraper.BoletinProvider{
		boe.NewProvider(),
	}

	// Obtenemos los documentos de hace 2 días para garantizar que ya estén publicados
	// (ej. a las 00:00 del mismo día, muchos boletines aún no existen).
	targetDate := time.Now().AddDate(0, 0, -2)

	for _, provider := range providers {
		log.Printf("=== Iniciando scraping para la fuente: %s ===", provider.Name())
		log.Printf("Obteniendo sumario %s para %s...\n", provider.Name(), targetDate.Format("2006-01-02"))

		ids, err := provider.FetchSummary(ctx, targetDate)
		if err != nil {
			log.Printf("Error obteniendo sumario de %s: %v\n", provider.Name(), err)
			continue
		}

		log.Printf("Encontrados %d documentos en el sumario de %s.\n", len(ids), provider.Name())

		// Procesar cada documento
		for _, id := range ids {
			log.Printf("Procesando documento %s...", id)

			doc, err := provider.FetchDocument(ctx, id)
			if err != nil {
				log.Printf("Error al procesar %s: %v\n", id, err)
				continue
			}

			// Guardar el documento estructurado en JSON
			err = blobStorage.SaveDocument(ctx, doc)
			if err != nil {
				log.Printf("Error guardando JSON para %s: %v\n", id, err)
				continue
			}
			log.Printf("Documento %s guardado con éxito.\n", id)
		}

		log.Printf("=== Scraping de la fuente %s completado ===", provider.Name())
	}

	log.Println("Proceso global de scraping completado exitosamente.")
}
