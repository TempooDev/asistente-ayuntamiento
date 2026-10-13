using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using FluentAssertions;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

/// <summary>The "Fuentes consultadas" appended to both pipelines' answers.</summary>
public class SourceCitationsTests
{
    [Fact]
    public void Each_document_is_listed_once_in_rank_order()
    {
        var sources = SourceCitations.Format([
            ("BOE-A-2026-2", "Orden de becas", "https://www.boe.es/buscar/doc.php?id=BOE-A-2026-2"),
            ("BOE-A-2026-1", "", "https://www.boe.es/buscar/doc.php?id=BOE-A-2026-1"),
            ("BOE-A-2026-2", "Orden de becas", "https://www.boe.es/buscar/doc.php?id=BOE-A-2026-2"),
        ]);

        sources.Should().Be("\n\n### Fuentes consultadas\n" +
            "- [Orden de becas](https://www.boe.es/buscar/doc.php?id=BOE-A-2026-2) (BOE-A-2026-2)\n" +
            "- [BOE-A-2026-1](https://www.boe.es/buscar/doc.php?id=BOE-A-2026-1)");
    }

    [Fact]
    public void Without_documents_there_is_no_section()
    {
        SourceCitations.Format([]).Should().BeEmpty();
        SourceCitations.Format([("", "Sin referencia", "json/x")]).Should().BeEmpty();
    }

    [Fact]
    public void A_title_with_brackets_or_line_breaks_does_not_break_the_link()
    {
        var sources = SourceCitations.Format([("BOE-A-2026-3", "Real Decreto [corrección]\nde errores", "https://www.boe.es/x")]);

        sources.Should().Contain("- [Real Decreto (corrección) de errores](https://www.boe.es/x) (BOE-A-2026-3)");
    }
}
