package main

import (
	"context"
	"fmt"
	"log"
	"net/http"
	"os"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/boe"
	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
	"github.com/asistente-ayuntamiento/go-scraper/internal/storage"
	"github.com/asistente-ayuntamiento/go-scraper/internal/telemetry"
	"github.com/asistente-ayuntamiento/go-scraper/internal/messaging"
)

var (
	blobStorage  storage.DocumentStorage
	providers    []scraper.BoletinProvider
	msgPublisher *messaging.Publisher
)

func main() {
	fmt.Println("Iniciando BOE Scraper...")

	ctx := context.Background()

	shutdown, err := telemetry.InitProvider(ctx)
	if err != nil {
		log.Printf("Error inicializando OpenTelemetry: %v\n", err)
	} else {
		defer shutdown(ctx)
	}

	blobStorage, err = storage.NewDocumentStorage(ctx)
	if err != nil {
		log.Printf("Error inicializando storage: %v\n", err)
		return
	}
	defer blobStorage.Close()

	msgPublisher, err = messaging.NewPublisher()
	if err != nil {
		log.Printf("Aviso: no se pudo inicializar RabbitMQ: %v\n", err)
	} else {
		defer msgPublisher.Close()
	}

	providers = []scraper.BoletinProvider{
		boe.NewProvider(),
	}

	// Ejecutar el scraping automático por defecto si hay env vars
	go runDefaultScraperWorkflow(ctx)

	// Endpoints HTTP
	http.HandleFunc("/health", func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
		w.Write([]byte("BOE Scraper is running\n"))
	})

	http.HandleFunc("/api/scrape", func(w http.ResponseWriter, r *http.Request) {
		startStr := r.URL.Query().Get("start")
		endStr := r.URL.Query().Get("end")

		if startStr == "" {
			http.Error(w, "El parámetro 'start' (YYYY-MM-DD) es requerido", http.StatusBadRequest)
			return
		}

		startDate, err := time.Parse("2006-01-02", startStr)
		if err != nil {
			http.Error(w, "Formato de 'start' inválido", http.StatusBadRequest)
			return
		}

		endDate := startDate
		if endStr != "" {
			endDate, err = time.Parse("2006-01-02", endStr)
			if err != nil {
				http.Error(w, "Formato de 'end' inválido", http.StatusBadRequest)
				return
			}
		}

		// Lanzamos el trabajo en background y respondemos inmediatamente
		go scrapeDateRange(context.Background(), startDate, endDate)
		
		w.WriteHeader(http.StatusAccepted)
		w.Write([]byte(fmt.Sprintf("Scraping encolado desde %s hasta %s\n", startDate.Format("2006-01-02"), endDate.Format("2006-01-02"))))
	})

	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}
	fmt.Printf("Servidor escuchando en puerto %s\n", port)
	if err := http.ListenAndServe(":"+port, nil); err != nil {
		log.Fatalf("Error al iniciar el servidor: %v", err)
	}
}

func runDefaultScraperWorkflow(ctx context.Context) {
	startDateStr := os.Getenv("SCRAPE_START_DATE")
	endDateStr := os.Getenv("SCRAPE_END_DATE")
	
	var startDate, endDate time.Time
	var err error

	if startDateStr != "" {
		startDate, err = time.Parse("2006-01-02", startDateStr)
		if err != nil {
			log.Printf("Error parseando SCRAPE_START_DATE: %v", err)
			return
		}
		if endDateStr != "" {
			endDate, err = time.Parse("2006-01-02", endDateStr)
			if err != nil {
				log.Printf("Error parseando SCRAPE_END_DATE: %v", err)
				return
			}
		} else {
			endDate = time.Now()
		}
	} else {
		// Por defecto
		targetDate := time.Now().AddDate(0, 0, -2)
		startDate = targetDate
		endDate = targetDate
	}

	scrapeDateRange(ctx, startDate, endDate)
}

func scrapeDateRange(ctx context.Context, startDate, endDate time.Time) {
	log.Printf("Iniciando scraping global desde %s hasta %s", startDate.Format("2006-01-02"), endDate.Format("2006-01-02"))

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

			for _, id := range ids {
				doc, err := provider.FetchDocument(ctx, id)
				if err != nil {
					log.Printf("Error al procesar %s: %v\n", id, err)
					continue
				}

				if err := blobStorage.SaveDocument(ctx, doc); err != nil {
					log.Printf("Error guardando JSON para %s: %v\n", id, err)
					continue
				}

				if msgPublisher != nil {
					_ = msgPublisher.PublishDocument(ctx, messaging.DocumentMessage{
						Source:     doc.Metadata.Source,
						DocumentID: doc.DocumentID,
						BlobPath:   fmt.Sprintf("json/%s/%s.json", doc.Metadata.Source, doc.DocumentID),
					})
				}
			}
			log.Printf("Día %s completado con éxito.\n", d.Format("2006-01-02"))
		}
		
		log.Printf("=== Scraping de la fuente %s completado ===", provider.Name())
	}

	log.Println("Proceso global de scraping completado exitosamente.")
}
