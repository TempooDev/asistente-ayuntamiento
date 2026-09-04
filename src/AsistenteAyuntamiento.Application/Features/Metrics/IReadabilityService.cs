namespace AsistenteAyuntamiento.Application.Features.Metrics;

public interface IReadabilityService
{
    double CalculateIfsz(string text);
    int CountSyllables(string text);
    int CountWords(string text);
    int CountSentences(string text);
}
