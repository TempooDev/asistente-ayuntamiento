package boja

import (
	"context"
	"encoding/xml"
	"fmt"
	"io"
	"net/http"
	"regexp"
	"strings"
	"time"

	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"go.opentelemetry.io/otel"
	"golang.org/x/time/rate"
)

// Provider implementa scraper.BoletinProvider para el BOJA.
type Provider struct {
	httpClient  *http.Client
	rateLimiter *rate.Limiter
	feeds       []string
	
	// Cache for binary search: year -> map[num]date
	dateCache map[int]map[int]time.Time
}

func (p *Provider) UpdateFeeds(feeds []string) {
	if len(feeds) > 0 {
		p.feeds = feeds
	}
}

func NewProvider(customFeeds ...string) *Provider {
	feeds := customFeeds
	if len(feeds) == 0 {
		feeds = []string{
			"https://www.juntadeandalucia.es/boja/distribucion/s51.xml",
			"https://www.juntadeandalucia.es/boja/distribucion/s52.xml",
			"https://www.juntadeandalucia.es/boja/distribucion/s53.xml",
			"https://www.juntadeandalucia.es/boja/distribucion/s54.xml",
			"https://www.juntadeandalucia.es/boja/distribucion/s55.xml",
		}
	}

	limiter := rate.NewLimiter(rate.Every(1*time.Second), 1)
	return &Provider{
		httpClient: &http.Client{
			Transport: otelhttp.NewTransport(http.DefaultTransport),
			Timeout:   30 * time.Second,
		},
		rateLimiter: limiter,
		feeds:       feeds,
		dateCache:   make(map[int]map[int]time.Time),
	}
}

func (p *Provider) Name() string {
	return "BOJA"
}

func (p *Provider) FetchSummary(ctx context.Context, date time.Time) ([]string, error) {
	ctx, span := otel.Tracer("boja-client").Start(ctx, "FetchSummary")
	defer span.End()

	// Si es el día de hoy, el XML es más rápido y estable.
	// Nota: El XML del BOJA no contiene fecha, siempre tiene el último.
	now := time.Now()
	if date.Year() == now.Year() && date.YearDay() == now.YearDay() {
		return p.fetchLatestFromXML(ctx)
	}

	// Para histórico, buscamos en el buscador HTML
	return p.fetchFromHTMLSearch(ctx, date)
}

func (p *Provider) fetchLatestFromXML(ctx context.Context) ([]string, error) {

	var ids []string
	for _, feed := range p.feeds {
		req, err := http.NewRequestWithContext(ctx, http.MethodGet, feed, nil)
		if err != nil {
			continue
		}

		resp, err := p.httpClient.Do(req)
		if err != nil {
			continue
		}
		defer resp.Body.Close()

		if resp.StatusCode != http.StatusOK {
			continue
		}

		var atom struct {
			Entries []struct {
				Link struct {
					Href string `xml:"href,attr"`
				} `xml:"link"`
			} `xml:"entry"`
		}

		if err := xml.NewDecoder(resp.Body).Decode(&atom); err == nil {
			for _, entry := range atom.Entries {
				if entry.Link.Href != "" {
					ids = append(ids, entry.Link.Href)
				}
			}
		}
	}

	return ids, nil
}

func (p *Provider) FetchDocument(ctx context.Context, id string) (*scraper.Document, []byte, error) {
	ctx, span := otel.Tracer("boja-client").Start(ctx, "FetchDocument")
	defer span.End()

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, id, nil)
	if err != nil {
		return nil, nil, err
	}

	resp, err := p.httpClient.Do(req)
	if err != nil {
		return nil, nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, nil, fmt.Errorf("status %d", resp.StatusCode)
	}

	htmlContent, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, nil, err
	}

	cleanText := scraper.StripHTMLTags(string(htmlContent))
	
	// Create a stable document ID from the URL
	// Example: https://www.juntadeandalucia.es/boja/2026/123/1
	docId := id
	parts := strings.Split(strings.TrimRight(id, "/"), "/")
	if len(parts) >= 3 {
		// e.g. boja/2026/123/1 -> BOJA-2026-123-1
		docId = "BOJA-" + strings.Join(parts[len(parts)-3:], "-")
		docId = strings.ReplaceAll(docId, ".html", "")
	}

	// Extract title from HTML
	titulo := "Documento BOJA"
	titleStart := strings.Index(string(htmlContent), "<title>")
	titleEnd := strings.Index(string(htmlContent), "</title>")
	if titleStart != -1 && titleEnd != -1 && titleEnd > titleStart {
		titulo = string(htmlContent)[titleStart+7 : titleEnd]
		titulo = strings.TrimSpace(strings.ReplaceAll(titulo, "\n", " "))
	}

	doc := &scraper.Document{
		DocumentID: docId,
		Metadata: scraper.Metadata{
			Source:           p.Name(),
			DocumentID:       docId,
			Titulo:           titulo,
			Departamento:     "Junta de Andalucía",
			FechaPublicacion: time.Now().Format("2006-01-02"),
		},
		Text: cleanText,
	}

	return doc, htmlContent, nil
}

func (p *Provider) fetchFromHTMLSearch(ctx context.Context, targetDate time.Time) ([]string, error) {
	year := targetDate.Year()
	
	// Asegurar caché para el año
	if p.dateCache[year] == nil {
		p.dateCache[year] = make(map[int]time.Time)
	}

	// Obtener el último número conocido a través del XML de hoy
	// O podríamos hacer un approach estático, por ejemplo máximo 300 boletines al año.
	// Pero para ser exactos, obtendremos el último BOJA desde s51.xml si es el año actual.
	maxNum := 300 
	
	latestURLs, err := p.fetchLatestFromXML(ctx)
	if err == nil && len(latestURLs) > 0 {
		// Parsear el NUM del URL: /boja/2026/167/1
		re := regexp.MustCompile(`/boja/(\d{4})/(\d+)/`)
		m := re.FindStringSubmatch(latestURLs[0])
		if len(m) == 3 {
			var currYear, currNum int
			fmt.Sscanf(m[1], "%d", &currYear)
			fmt.Sscanf(m[2], "%d", &currNum)
			if currYear == year {
				maxNum = currNum
			}
		}
	}

	low := 1
	high := maxNum
	var bestMatchURLs []string
	
	// Función helper para obtener fecha y URLs de un boletín
	fetchBulletin := func(num int) (time.Time, []string, error) {
		if cachedDate, ok := p.dateCache[year][num]; ok {
			// Si ya sabemos la fecha pero necesitamos las URLs, tenemos que bajarlas.
			// Optimización: si no es la fecha target, no bajamos las URLs.
			if !cachedDate.Equal(targetDate) {
				return cachedDate, nil, nil
			}
		}
		
		url := fmt.Sprintf("https://www.juntadeandalucia.es/boja/%d/%d/index.html", year, num)
		req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
		if err != nil { return time.Time{}, nil, err }
		
		resp, err := p.httpClient.Do(req)
		if err != nil { return time.Time{}, nil, err }
		defer resp.Body.Close()
		
		if resp.StatusCode == http.StatusNotFound {
			return time.Time{}, nil, fmt.Errorf("404")
		}
		if resp.StatusCode != http.StatusOK {
			return time.Time{}, nil, fmt.Errorf("status %d", resp.StatusCode)
		}
		
		bodyBytes, err := io.ReadAll(resp.Body)
		if err != nil { return time.Time{}, nil, err }
		htmlContent := string(bodyBytes)
		
		// Extraer fecha: <p class="titular">BOJA nº 130 de 08/07/2026</p>
		reDate := regexp.MustCompile(`BOJA nº \d+(?:\.\d+)? de (\d{2}/\d{2}/\d{4})`)
		mDate := reDate.FindStringSubmatch(htmlContent)
		if len(mDate) < 2 {
			return time.Time{}, nil, fmt.Errorf("no date found")
		}
		
		parsedDate, err := time.Parse("02/01/2006", mDate[1])
		if err != nil { return time.Time{}, nil, err }
		
		p.dateCache[year][num] = parsedDate
		
		if parsedDate.Equal(targetDate) {
			reLinks := regexp.MustCompile(fmt.Sprintf(`href="(https?://www\.juntadeandalucia\.es/boja/%d/%d/\d+(?:\.html)?)"`, year, num))
			matches := reLinks.FindAllStringSubmatch(htmlContent, -1)
			
			// Try relative links too
			if len(matches) == 0 {
				reLinksRel := regexp.MustCompile(fmt.Sprintf(`href="(/boja/%d/%d/\d+(?:\.html)?)"`, year, num))
				matchesRel := reLinksRel.FindAllStringSubmatch(htmlContent, -1)
				for _, m := range matchesRel {
					bestMatchURLs = append(bestMatchURLs, "https://www.juntadeandalucia.es"+m[1])
				}
			} else {
				for _, m := range matches {
					bestMatchURLs = append(bestMatchURLs, m[1])
				}
			}
		}
		
		return parsedDate, bestMatchURLs, nil
	}
	
	// Asegurarnos de que el high existe, si no, bajarlo
	for high > 0 {
		_, _, err := fetchBulletin(high)
		if err == nil {
			break
		}
		high--
	}
	if high == 0 {
		return nil, nil // No hay boletines
	}

	for low <= high {
		mid := (low + high) / 2
		dateMid, urls, err := fetchBulletin(mid)
		if err != nil {
			// Si falla un mid intermedio (ej. 404), asumimos que hay hueco.
			// Hacemos una búsqueda lineal rápida hacia abajo para encontrar un válido.
			validMid := mid - 1
			for validMid >= low {
				dateMid, urls, err = fetchBulletin(validMid)
				if err == nil { break }
				validMid--
			}
			if validMid < low {
				low = mid + 1
				continue
			}
			mid = validMid
		}
		
		if dateMid.Equal(targetDate) {
			// Lo encontramos
			// Filter unique
			seen := make(map[string]bool)
			var uniqueURLs []string
			for _, u := range urls {
				if !seen[u] {
					seen[u] = true
					uniqueURLs = append(uniqueURLs, u)
				}
			}
			return uniqueURLs, nil
		}
		
		if dateMid.Before(targetDate) {
			low = mid + 1
		} else {
			high = mid - 1
		}
	}

	// No se publicó BOJA ese día (o fue un fin de semana/festivo)
	return nil, nil
}
