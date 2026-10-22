using AsistenteAyuntamiento.Application.Features.Retrieval;
using FluentAssertions;

namespace AsistenteAyuntamiento.Application.Tests.Features.Retrieval;

public class QueryEmbeddingOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_an_instruction_the_query_is_unchanged(string? instruction) =>
        new QueryEmbeddingOptions(instruction).Format("¿Plazo?").Should().Be("¿Plazo?");

    [Fact]
    public void With_an_instruction_the_query_uses_the_qwen_format() =>
        new QueryEmbeddingOptions(" Retrieve passages ").Format("¿Plazo?").Should().Be("Instruct: Retrieve passages\nQuery: ¿Plazo?");
}
