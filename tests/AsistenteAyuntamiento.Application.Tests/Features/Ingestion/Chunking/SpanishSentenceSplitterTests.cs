using AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;
using FluentAssertions;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion.Chunking;

/// <summary>Sentence splitting for the semantic strategy, aware of the abbreviations common in bulletins.</summary>
public class SpanishSentenceSplitterTests
{
    [Fact]
    public void An_article_abbreviation_is_not_a_sentence_end()
    {
        var sentences = SpanishSentenceSplitter.Split("El plazo se fija conforme al art. 25 de la Ley 7/1985. El solicitante presentará la solicitud.");

        sentences.Should().Equal("El plazo se fija conforme al art. 25 de la Ley 7/1985.", "El solicitante presentará la solicitud.");
    }

    [Theory]
    [InlineData("Comparece D. Juan Pérez ante la Sra. Martínez y Dña. María López.")]
    [InlineData("Publicado en el BOJA núm. 12, apdo. 3, de los arts. 4 y 5.")]
    [InlineData("Lo dispuesto en el artículo 1.º y en el 2.ª se aplica a todos.")]
    [InlineData("Firmado por el Excmo. Sr. Alcalde, J. García.")]
    [InlineData("Licitación de la empresa Servicios Municipales S.A. Con domicilio en Málaga.")]
    [InlineData("Se exige documentación, etc. Y se presentará en plazo.")]
    public void Common_abbreviations_keep_the_sentence_whole(string text) =>
        SpanishSentenceSplitter.Split(text).Should().ContainSingle().Which.Should().Be(text);

    [Fact]
    public void Questions_exclamations_and_blank_lines_end_sentences()
    {
        var sentences = SpanishSentenceSplitter.Split("¿Qué plazo hay? Diez días hábiles. ¡Atención! Consulte el anexo\n\nAnexo I\nRelación de admitidos");

        sentences.Should().Equal("¿Qué plazo hay?", "Diez días hábiles.", "¡Atención!", "Consulte el anexo", "Anexo I\nRelación de admitidos");
    }

    [Fact]
    public void A_period_followed_by_a_lowercase_word_is_not_an_end()
    {
        SpanishSentenceSplitter.Split("Se destina a la partida 3. con cargo al presupuesto.").Should().ContainSingle();
    }

    [Fact]
    public void A_number_with_thousands_separator_can_end_a_sentence()
    {
        SpanishSentenceSplitter.Split("El importe asciende a 3.000. El pago es único.")
            .Should().Equal("El importe asciende a 3.000.", "El pago es único.");
    }

    [Fact]
    public void Closing_quotes_stay_with_their_sentence()
    {
        SpanishSentenceSplitter.Split("Se denomina «Plan Local.» El plan entra en vigor.")
            .Should().Equal("Se denomina «Plan Local.»", "El plan entra en vigor.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public void Empty_text_has_no_sentences(string text) =>
        SpanishSentenceSplitter.Split(text).Should().BeEmpty();
}
