using System.Text.Json;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using FluentAssertions;

namespace AsistenteAyuntamiento.Application.Tests.Features.Retrieval;

public class QueryExpansionParsingTests
{
    [Fact]
    public void Parses_the_json_keys_the_prompt_asks_for()
    {
        var parsed = QueryExpansionService.Parse(
            """{"query_lexica": "subvencion & vivienda", "query_semantica": "Ayudas para vivienda joven", "filtro_municipio": "Málaga"}""",
            "ayudas piso");

        parsed.LexicalQuery.Should().Be("subvencion & vivienda");
        parsed.SemanticQuery.Should().Be("Ayudas para vivienda joven");
        parsed.MunicipalityFilter.Should().Be("Málaga");
    }

    [Theory]
    [InlineData("```json\n{\"query_lexica\": \"a & b\", \"query_semantica\": \"c\"}\n```")]
    [InlineData("```\n{\"query_lexica\": \"a & b\", \"query_semantica\": \"c\"}\n```")]
    [InlineData("  {\"query_lexica\": \"a & b\", \"query_semantica\": \"c\"}  ")]
    public void Accepts_markdown_fences_and_whitespace(string answer)
    {
        var parsed = QueryExpansionService.Parse(answer, "pregunta");

        parsed.LexicalQuery.Should().Be("a & b");
        parsed.SemanticQuery.Should().Be("c");
        parsed.MunicipalityFilter.Should().BeNull();
    }

    [Fact]
    public void Missing_or_blank_fields_fall_back_to_the_user_query()
    {
        var parsed = QueryExpansionService.Parse("""{"query_lexica": "", "filtro_municipio": "  "}""", "plazo matrícula");

        parsed.LexicalQuery.Should().Be("plazo matrícula");
        parsed.SemanticQuery.Should().Be("plazo matrícula");
        parsed.MunicipalityFilter.Should().BeNull();
    }

    [Fact]
    public void Invalid_json_throws_so_the_service_uses_its_fallback()
    {
        var act = () => QueryExpansionService.Parse("Lo siento, no puedo ayudar.", "x");

        act.Should().Throw<JsonException>();
        QueryExpansionService.Fallback("plazo  matrícula").LexicalQuery.Should().Be("plazo & matrícula");
    }
}
