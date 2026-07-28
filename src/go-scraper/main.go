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

	// Determinamos el rango de fechas a extraer
	startDateStr := os.Getenv("SCRAPE_START_DATE")
	endDateStr := os.Getenv("SCRAPE_END_DATE")
	
	var startDate, endDate time.Time
	var err error

	if startDateStr != "" {
		startDate, err = time.Parse("2006-01-02", startDateStr)
		if err != nil {
			log.Fatalf("Error parseando SCRAPE_START_DATE (formato YYYY-MM-DD): %v", err)
		}
		if endDateStr != "" {
			endDate, err = time.Parse("2006-01-02", endDateStr)
			if err != nil {
				log.Fatalf("Error parseando SCRAPE_END_DATE (formato YYYY-MM-DD): %v", err)
			}
		} else {
			endDate = time.Now()
		}
	} else {
		// Por defecto, descargamos el boletín de hace 2 días para garantizar publicación
		targetDate := time.Now().AddDate(0, 0, -2)
		startDate = targetDate
		endDate = targetDate
	}

	for _, provider := range providers {
		log.Printf("=== Iniciando scraping para la fuente: %s ===", provider.Name())

		for d := startDate; !d.After(endDate); d = d.AddDate(0, 0, 1) {
			log.Printf("--- Procesando %s para la fecha: %s ---", provider.Name(), d.Format("2006-01-02"))

			ids, err := provider.FetchSummary(ctx, d)
			if err != nil {
				log.Printf("Error obteniendo sumario de %s para %s: %v\n", provider.Name(), d.Format("2006-01-02"), err)
				continue
			}

			if len(ids) == 0 {
				log.Printf("No hay documentos en %s para %s.\n", provider.Name(), d.Format("2006-01-02"))
				continue
			}

			log.Printf("Encontrados %d documentos en el sumario.\n", len(ids))

			// Procesar cada documento
			for _, id := range ids {
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
			}
			log.Printf("Día %s completado con éxito.\n", d.Format("2006-01-02"))
		}
		
		log.Printf("=== Scraping de la fuente %s completado ===", provider.Name())
	}

	log.Println("Proceso global de scraping completado exitosamente.")
}
