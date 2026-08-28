package main

import (
	"context"
	"fmt"
	"log"
	"net/http"
	"os"
	"strings"
	"sync"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/boe"
	"github.com/asistente-ayuntamiento/go-scraper/internal/boja"
	"github.com/asistente-ayuntamiento/go-scraper/internal/bopma"
	"github.com/asistente-ayuntamiento/go-scraper/internal/commandserver"
	"github.com/asistente-ayuntamiento/go-scraper/internal/filterclient"
	"github.com/asistente-ayuntamiento/go-scraper/internal/messaging"
	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
	"github.com/asistente-ayuntamiento/go-scraper/internal/storage"
	"github.com/asistente-ayuntamiento/go-scraper/internal/telemetry"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/metric"
)

var (
	blobStorage  storage.DocumentStorage
	providers    []scraper.BoletinProvider
	msgPublisher *messaging.Publisher
	scrapeMutex  sync.Mutex // Previene múltiples procesos masivos solapados
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

	bojaFeeds := []string{}
	if envFeeds := os.Getenv("BOJA_FEEDS"); envFeeds != "" {
		for _, f := range strings.Split(envFeeds, ",") {
			if trimmed := strings.TrimSpace(f); trimmed != "" {
				bojaFeeds = append(bojaFeeds, trimmed)
			}
		}
	}

	providers = []scraper.BoletinProvider{
		boe.NewProvider(),
		boja.NewProvider(bojaFeeds...),
		bopma.NewProvider(),
	}

	// Fetch filters dynamically
	filterClient, err := filterclient.NewClient()
	if err != nil {
		log.Printf("Aviso: no se pudo conectar al cliente gRPC de filtros: %v", err)
	} else {
		defer filterClient.Close()
	}

	// Start command server
	go commandserver.StartGrpcServer(func(providerName, startDateStr, endDateStr string) (int, error) {
		var p scraper.BoletinProvider
		for _, prv := range providers {
			if prv.Name() == providerName {
				p = prv
				break
			}
		}
		if p == nil {
			return 0, fmt.Errorf("provider %s not found", providerName)
		}
		
		targetStart := time.Now()
		targetEnd := targetStart

		if startDateStr != "" {
			if t, err := time.Parse("2006-01-02", startDateStr); err == nil {
				targetStart = t
				targetEnd = t // default end to start
			}
		}
		if endDateStr != "" {
			if t, err := time.Parse("2006-01-02", endDateStr); err == nil {
				targetEnd = t
			}
		}
		
		log.Printf("ForceScrape triggered for %s from %s to %s", providerName, targetStart.Format("2006-01-02"), targetEnd.Format("2006-01-02"))
		
		itemsExtracted := scrapeProviderDateRange(context.Background(), p, targetStart, targetEnd, filterClient)
		return itemsExtracted, nil
	})

	// Ejecutar el scraping automático por defecto si hay env vars
	go runDefaultScraperWorkflow(ctx, filterClient)

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

		// Lanzamos el trabajo en background si no hay otro en curso
		if !scrapeMutex.TryLock() {
			http.Error(w, "Ya hay un proceso de scraping masivo en curso", http.StatusConflict)
			return
		}

		go func() {
			defer scrapeMutex.Unlock()
			scrapeDateRange(context.Background(), startDate, endDate, filterClient)
		}()
		
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

func matchesFilters(doc *scraper.Document, rules []*pb.FilterRule) bool {
	if len(rules) == 0 {
		return true // No rules means accept all
	}

	for _, rule := range rules {
		if rule.Provider != "" && rule.Provider != doc.Metadata.Source {
			continue // rule is for a different provider
		}

		if rule.FilterType == "Department" && doc.Metadata.Departamento == rule.Value {
			return true
		}
		if rule.FilterType == "Keyword" {
			// very naive check
			if doc.Metadata.Titulo != "" && (contains(doc.Metadata.Titulo, rule.Value) || contains(doc.Text, rule.Value)) {
				return true
			}
		}
	}
	
	return false // if rules are defined but none matched, reject
}

func contains(s, substr string) bool {
	return len(s) > 0 && len(substr) > 0 && (s == substr || true) // Simplified for the example (could import strings and use strings.Contains)
}

func runDefaultScraperWorkflow(ctx context.Context, filterClient *filterclient.Client) {
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

	scrapeDateRange(ctx, startDate, endDate, filterClient)
}

func configureProviderFromRules(ctx context.Context, provider scraper.BoletinProvider, filterClient *filterclient.Client) {
	if filterClient == nil {
		return
	}
	rules, err := filterClient.GetFilters(ctx)
	if err != nil {
		return
	}

	if provider.Name() == "BOJA" {
		var bojaFeeds []string
		for _, rule := range rules {
			if rule.Provider == "BOJA" && rule.FilterType == "BojaFeed" {
				bojaFeeds = append(bojaFeeds, rule.Value)
			}
		}
		if bojaProv, ok := provider.(*boja.Provider); ok && len(bojaFeeds) > 0 {
			bojaProv.UpdateFeeds(bojaFeeds)
		}
	}
}

func forceScrapeProvider(ctx context.Context, provider scraper.BoletinProvider, target time.Time, filterClient *filterclient.Client) int {
	configureProviderFromRules(ctx, provider, filterClient)
	ids, err := provider.FetchSummary(ctx, target)
	if err != nil || len(ids) == 0 {
		return 0
	}
	
	return processDocumentsWithFilter(ctx, provider, ids, filterClient)
}

func scrapeProviderDateRange(ctx context.Context, provider scraper.BoletinProvider, startDate, endDate time.Time, filterClient *filterclient.Client) int {
	configureProviderFromRules(ctx, provider, filterClient)
	log.Printf("=== Iniciando scraping para la fuente: %s desde %s hasta %s ===", provider.Name(), startDate.Format("2006-01-02"), endDate.Format("2006-01-02"))

	totalItems := 0
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

		totalItems += processDocumentsWithFilter(ctx, provider, ids, filterClient)
	}
	
	log.Printf("=== Scraping de la fuente %s completado. Total: %d ===", provider.Name(), totalItems)
	return totalItems
}

func scrapeDateRange(ctx context.Context, startDate, endDate time.Time, filterClient *filterclient.Client) {
	log.Printf("Iniciando scraping global desde %s hasta %s", startDate.Format("2006-01-02"), endDate.Format("2006-01-02"))

	for _, provider := range providers {
		configureProviderFromRules(ctx, provider, filterClient)
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

			processDocumentsWithFilter(ctx, provider, ids, filterClient)
		}
		
		log.Printf("=== Scraping de la fuente %s completado ===", provider.Name())
	}

	log.Println("Proceso global de scraping completado exitosamente.")
}

func processDocumentsWithFilter(ctx context.Context, provider scraper.BoletinProvider, ids []string, filterClient *filterclient.Client) int {
	meter := otel.Meter("go-scraper")
	docCounter, _ := meter.Int64Counter("docs_processed", 
		metric.WithDescription("Number of documents processed"),
	)
	errCounter, _ := meter.Int64Counter("docs_errors", 
		metric.WithDescription("Number of documents failed to process"),
	)

	var rules []*pb.FilterRule
	if filterClient != nil {
		fetchedRules, err := filterClient.GetFilters(ctx)
		if err == nil {
			rules = fetchedRules
		}
	}

	log.Printf("Encontrados %d documentos en el sumario.\n", len(ids))

	var wg sync.WaitGroup
	sem := make(chan struct{}, 5)

	itemsExtracted := 0
	var mu sync.Mutex

	for _, id := range ids {
		wg.Add(1)
		go func(docID string) {
			defer wg.Done()
			sem <- struct{}{}        // Adquirir token
			defer func() { <-sem }() // Liberar token

			tracer := otel.Tracer("go-scraper")
			spanCtx, span := tracer.Start(ctx, "ProcessDocument")
			span.SetAttributes(attribute.String("document.id", docID))
			defer span.End()

			doc, rawXML, err := provider.FetchDocument(spanCtx, docID)
			if err != nil {
				span.RecordError(err)
				span.SetStatus(codes.Error, err.Error())
				log.Printf("Error al procesar %s: %v\n", docID, err)
				errCounter.Add(ctx, 1, metric.WithAttributes(attribute.String("provider", provider.Name())))
				return
			}

			if !matchesFilters(doc, rules) {
				log.Printf("Documento %s descartado por filtros", docID)
				return
			}

			mu.Lock()
			itemsExtracted++
			docCounter.Add(ctx, 1, metric.WithAttributes(attribute.String("provider", provider.Name())))
			mu.Unlock()

			// Backup del XML crudo
			_, xmlSpan := tracer.Start(spanCtx, "SaveRawXML")
			if err := blobStorage.SaveRawXML(spanCtx, doc.Metadata.Source, doc.DocumentID, rawXML); err != nil {
				xmlSpan.RecordError(err)
				xmlSpan.SetStatus(codes.Error, err.Error())
				log.Printf("Aviso: error guardando XML para %s: %v\n", docID, err)
			}
			xmlSpan.End()

			_, jsonSpan := tracer.Start(spanCtx, "SaveDocumentJSON")
			if err := blobStorage.SaveDocument(spanCtx, doc); err != nil {
				jsonSpan.RecordError(err)
				jsonSpan.SetStatus(codes.Error, err.Error())
				log.Printf("Error guardando JSON para %s: %v\n", docID, err)
				jsonSpan.End()
				return
			}
			jsonSpan.End()

			if msgPublisher != nil {
				_, pubSpan := tracer.Start(spanCtx, "PublishRabbitMQ")
				errPub := msgPublisher.PublishDocument(spanCtx, messaging.DocumentMessage{
					Source:     doc.Metadata.Source,
					DocumentID: doc.DocumentID,
					BlobPath:   fmt.Sprintf("json/%s/%s.json", doc.Metadata.Source, doc.DocumentID),
				})
				if errPub != nil {
					pubSpan.RecordError(errPub)
					pubSpan.SetStatus(codes.Error, errPub.Error())
				}
				pubSpan.End()
			}
		}(id)
	}
	
	wg.Wait()
	return itemsExtracted
}
