using AsistenteAyuntamiento.Application.Features.Users;
using FluentAssertions;

namespace AsistenteAyuntamiento.Application.Tests.Features.Users;

public class HistoryAnalyzerParsingTests
{
    [Fact]
    public void Reads_topics_and_locations_inside_markdown_fences()
    {
        var parsed = HistoryAnalyzerService.Parse("```json\n{\"topics\": [\"vivienda\"], \"locations\": [\"Málaga\"]}\n```");

        parsed!.Topics.Should().Equal("vivienda");
        parsed.Locations.Should().Equal("Málaga");
    }

    [Theory]
    [InlineData("""{"Topics": null, "Locations": ["Ronda"]}""", 0, 1)]
    [InlineData("""{"Topics": ["empleo"], "Locations": null}""", 1, 0)]
    [InlineData("""{"Topics": null, "Locations": null}""", 0, 0)]
    [InlineData("""{}""", 0, 0)]
    public void Null_or_missing_lists_are_read_as_empty(string answer, int topics, int locations)
    {
        var parsed = HistoryAnalyzerService.Parse(answer);

        parsed.Should().NotBeNull();
        parsed!.Topics.Should().HaveCount(topics);
        parsed.Locations.Should().HaveCount(locations);
    }

    [Theory]
    [InlineData("no es JSON")]
    [InlineData("null")]
    public void Invalid_or_null_json_is_ignored(string answer)
    {
        HistoryAnalyzerService.Parse(answer).Should().BeNull();
    }
}
