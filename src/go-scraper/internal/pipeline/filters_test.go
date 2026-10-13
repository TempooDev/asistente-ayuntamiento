package pipeline

import (
	"testing"

	pb "github.com/asistente-ayuntamiento/go-scraper/internal/protos"
	"github.com/asistente-ayuntamiento/go-scraper/internal/scraper"
)

func doc(source, title, department, text string) *scraper.Document {
	return &scraper.Document{
		Metadata: scraper.Metadata{Source: source, Titulo: title, Departamento: department},
		Text:     text,
	}
}

func rule(provider, filterType, value string) *pb.FilterRule {
	return &pb.FilterRule{Provider: provider, FilterType: filterType, Value: value}
}

func TestMatchesFilters(t *testing.T) {
	tests := []struct {
		name  string
		doc   *scraper.Document
		rules []*pb.FilterRule
		want  bool
	}{
		{
			name: "no rules accepts everything",
			doc:  doc("BOE", "Nombramiento de personal", "", ""),
			want: true,
		},
		{
			name:  "keyword matches title ignoring case and accents",
			doc:   doc("BOE", "Bases de SUBVENCIONES para ayuntamientos", "", ""),
			rules: []*pb.FilterRule{rule("", "Keyword", "subvención")},
			want:  true,
		},
		{
			name:  "keyword matches body text",
			doc:   doc("BOE", "Resolución de 3 de marzo", "", "se convocan ayudas al alquiler"),
			rules: []*pb.FilterRule{rule("BOE", "Keyword", "alquiler")},
			want:  true,
		},
		{
			name:  "keyword that does not appear rejects the document",
			doc:   doc("BOE", "Nombramiento de personal", "", "toma de posesión"),
			rules: []*pb.FilterRule{rule("", "Keyword", "subvención")},
			want:  false,
		},
		{
			name:  "department must match exactly (normalized)",
			doc:   doc("BOE", "Orden", "Ministerio de Hacienda", ""),
			rules: []*pb.FilterRule{rule("BOE", "Department", "ministerio de hacienda")},
			want:  true,
		},
		{
			name:  "department mismatch rejects",
			doc:   doc("BOE", "Orden", "Ministerio de Defensa", ""),
			rules: []*pb.FilterRule{rule("BOE", "Department", "Ministerio de Hacienda")},
			want:  false,
		},
		{
			name:  "rules of another provider do not apply",
			doc:   doc("BOE", "Nombramiento", "", ""),
			rules: []*pb.FilterRule{rule("BOJA", "Keyword", "subvención")},
			want:  true,
		},
		{
			name:  "only configuration rules (BojaFeed) never reject documents",
			doc:   doc("BOE", "Nombramiento", "", ""),
			rules: []*pb.FilterRule{rule("BOJA", "BojaFeed", "s51")},
			want:  true,
		},
		{
			name:  "section rules configure fetching, they do not filter content",
			doc:   doc("BOE", "Nombramiento", "", ""),
			rules: []*pb.FilterRule{rule("BOE", "Section", "2B")},
			want:  true,
		},
		{
			name:  "empty keyword value is ignored instead of rejecting everything",
			doc:   doc("BOE", "Nombramiento", "", ""),
			rules: []*pb.FilterRule{rule("BOE", "Keyword", "  ")},
			want:  true,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			if got := matchesFilters(tt.doc, tt.rules); got != tt.want {
				t.Errorf("matchesFilters() = %v, want %v", got, tt.want)
			}
		})
	}
}
