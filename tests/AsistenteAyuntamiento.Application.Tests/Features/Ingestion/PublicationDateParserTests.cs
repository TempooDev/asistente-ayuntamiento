using AsistenteAyuntamiento.Application.Features.Ingestion;
using FluentAssertions;
using Xunit;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion;

public class PublicationDateParserTests
{
    [Theory]
    [InlineData("20260915")]            // BOE XML fecha_publicacion
    [InlineData("2026-09-15")]          // BOJA / BOPMA
    [InlineData("2026-09-15T00:00:00Z")]
    public void Parses_the_formats_written_by_the_scraper(string value)
    {
        PublicationDateParser.TryParse(value, out var date).Should().BeTrue();

        date.Should().Be(new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc));
        date.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("15/09/2026")]
    [InlineData("not a date")]
    public void Rejects_missing_or_unknown_formats(string? value) =>
        PublicationDateParser.TryParse(value, out _).Should().BeFalse();
}
