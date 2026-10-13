using System.Text.Json;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;

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

    [Fact]
    public void The_fallback_of_a_follow_up_searches_with_the_previous_questions()
    {
        var previous = new List<ChatMessageContent>
        {
            new(AuthorRole.User, "Convenio entre el Ministerio de Sanidad y la Comunidad de Madrid"),
            new(AuthorRole.Assistant, "El convenio BOE-A-2026-18557..."),
        };

        var fallback = QueryExpansionService.Fallback("¿Quién preside la Comisión de Seguimiento?", previous);

        fallback.SemanticQuery.Should().Be("Convenio entre el Ministerio de Sanidad y la Comunidad de Madrid\n¿Quién preside la Comisión de Seguimiento?");
        fallback.LexicalQuery.Should().NotContain("Sanidad", "the lexical query requires every word, so it stays on the question");
    }

    [Fact]
    public async Task The_expansion_prompt_carries_the_conversation()
    {
        string? prompt = null;
        var chat = new Mock<IChatCompletionService>();
        chat.Setup(c => c.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .Callback<ChatHistory, PromptExecutionSettings?, Kernel?, CancellationToken>((history, _, _, _) => prompt = history[^1].Content)
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, """{"query_lexica": "comision & seguimiento", "query_semantica": "Comisión de Seguimiento del Convenio BOE-A-2026-18557"}""")]);
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(chat.Object);
        var sut = new QueryExpansionService(builder.Build(), new ConfigurationBuilder().Build(), Mock.Of<ILlmUsageMetrics>(),
            Mock.Of<IAiCallRecorder>(), NullLogger<QueryExpansionService>.Instance);

        var expanded = await sut.ExpandQueryAsync("¿Quién preside la Comisión de Seguimiento?",
            [new(AuthorRole.User, "Háblame del convenio BOE-A-2026-18557")], new LlmCallScope("org_A", LlmPipelines.Hierarchical));

        prompt.Should().Contain("Usuario: Háblame del convenio BOE-A-2026-18557").And.Contain("¿Quién preside la Comisión de Seguimiento?");
        expanded.SemanticQuery.Should().Contain("BOE-A-2026-18557");
    }
}
