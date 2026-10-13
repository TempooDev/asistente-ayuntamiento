using AsistenteAyuntamiento.Application.Features.Metrics;
using FluentAssertions;

namespace AsistenteAyuntamiento.Application.Tests.Features.Metrics;

/// <summary>Flesch-Szigriszt readability for Spanish text.</summary>
public class ReadabilityServiceTests
{
    private readonly ReadabilityService _sut = new();

    [Theory]
    [InlineData("casa", 2)]
    [InlineData("ciudad", 2)]          // diphthong "iu"
    [InlineData("país", 2)]            // hiatus with accent
    [InlineData("buey", 1)]            // triphthong with final y
    [InlineData("ahora", 3)]           // silent h
    [InlineData("administración", 5)]
    public void Counts_spanish_syllables(string word, int expected)
    {
        _sut.CountSyllables(word).Should().Be(expected);
    }

    [Fact]
    public void Counts_sentences_and_words()
    {
        const string text = "El plazo termina hoy. ¿Necesitas ayuda? Llama al ayuntamiento!";

        _sut.CountSentences(text).Should().Be(3);
        _sut.CountWords(text).Should().Be(9);
    }

    [Fact]
    public void Plain_language_scores_higher_than_administrative_jargon()
    {
        var plain = _sut.CalculateIfsz("Pide la ayuda antes del lunes. Es gratis. Te la dan en una semana.");
        var jargon = _sut.CalculateIfsz("La tramitación del procedimiento administrativo de concesión de subvenciones se realizará mediante concurrencia competitiva conforme a la normativa reguladora correspondiente.");

        plain.Should().BeGreaterThan(jargon);
    }

    [Fact]
    public void Empty_text_scores_zero()
    {
        _sut.CalculateIfsz("   ").Should().Be(0);
    }
}
