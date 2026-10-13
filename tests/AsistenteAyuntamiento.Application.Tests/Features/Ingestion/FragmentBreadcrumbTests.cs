using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using FluentAssertions;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion;

/// <summary>The breadcrumb names the real sub-section, or none (spec: hierarchical-retrieval).</summary>
public class FragmentBreadcrumbTests
{
    [Fact]
    public void A_split_article_names_its_part_as_sub_section() =>
        FragmentEnrichmentService.BuildBreadcrumb(BulletinType.BOE, "Ministerio", "Ley 1/2026", "a5", "parte 2")
            .Should().Be("[BOLETÍN: BOE | ORGANISMO: Ministerio | NORMA: Ley 1/2026 | ARTÍCULO: a5 | SUBSECCIÓN: parte 2]");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_a_sub_section_the_segment_is_omitted(string? subSection) =>
        FragmentEnrichmentService.BuildBreadcrumb(BulletinType.BOJA, "Junta de Andalucía", "Orden", "Sección 1", subSection)
            .Should().Be("[BOLETÍN: BOJA | ORGANISMO: Junta de Andalucía | NORMA: Orden | ARTÍCULO: Sección 1]");
}
