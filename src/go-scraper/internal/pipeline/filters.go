package pipeline

import (
	"strings"

	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
)

// configurationFilterTypes configure what is fetched (BOJA feeds, BOE sections) instead of
// filtering documents: a document carries no feed/section metadata to compare against.
var configurationFilterTypes = map[string]bool{
	"BojaFeed": true,
	"Section":  true,
}

// accentReplacer folds Spanish accents so "subvención" matches "SUBVENCIONES".
var accentReplacer = strings.NewReplacer(
	"á", "a", "é", "e", "í", "i", "ó", "o", "ú", "u", "ü", "u",
)

func normalize(s string) string {
	return accentReplacer.Replace(strings.ToLower(strings.TrimSpace(s)))
}

// contentRulesFor returns the rules that filter documents of the given source.
func contentRulesFor(source string, rules []*pb.FilterRule) []*pb.FilterRule {
	var applicable []*pb.FilterRule
	for _, rule := range rules {
		if configurationFilterTypes[rule.FilterType] || normalize(rule.Value) == "" {
			continue
		}
		if rule.Provider != "" && !strings.EqualFold(rule.Provider, source) {
			continue
		}
		applicable = append(applicable, rule)
	}
	return applicable
}

// matchesFilters reports whether a document passes the content rules of its source.
// Without content rules for that source every document passes; otherwise at least one rule must match.
func matchesFilters(doc *scraper.Document, rules []*pb.FilterRule) bool {
	applicable := contentRulesFor(doc.Metadata.Source, rules)
	if len(applicable) == 0 {
		return true
	}

	title := normalize(doc.Metadata.Titulo)
	text := normalize(doc.Text)
	department := normalize(doc.Metadata.Departamento)

	for _, rule := range applicable {
		value := normalize(rule.Value)

		switch rule.FilterType {
		case "Department":
			if department == value {
				return true
			}
		case "Keyword":
			if strings.Contains(title, value) || strings.Contains(text, value) {
				return true
			}
		}
	}

	return false
}
