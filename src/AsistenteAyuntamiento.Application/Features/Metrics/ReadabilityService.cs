using System.Text.RegularExpressions;

namespace AsistenteAyuntamiento.Application.Features.Metrics;

public partial class ReadabilityService : IReadabilityService
{
    public double CalculateIfsz(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        double syllables = CountSyllables(text);
        double words = CountWords(text);
        double sentences = CountSentences(text);

        if (words == 0 || sentences == 0) return 0;

        // Fórmua del Índice Flesch-Szigriszt (IFSZ)
        return 206.835 - (62.3 * (syllables / words)) - (words / sentences);
    }

    public int CountSentences(string text)
    {
        // Dividir por puntos, exclamaciones, interrogaciones (excluyendo los que están en blanco)
        var matches = SentenceRegex().Matches(text);
        int count = 0;
        foreach (Match match in matches)
        {
            if (!string.IsNullOrWhiteSpace(match.Value)) count++;
        }
        return count == 0 ? 1 : count; // Mínimo 1 frase si hay palabras
    }

    public int CountWords(string text)
    {
        var matches = WordRegex().Matches(text);
        return matches.Count == 0 ? 1 : matches.Count; 
    }

    public int CountSyllables(string text)
    {
        var words = WordRegex().Matches(text.ToLowerInvariant());
        int totalSyllables = 0;

        foreach (Match m in words)
        {
            totalSyllables += CountWordSyllables(m.Value);
        }

        return totalSyllables;
    }

    private int CountWordSyllables(string word)
    {
        // Convert "y" acting as vowel to "i"
        if (word.EndsWith("y"))
        {
            word = word.Substring(0, word.Length - 1) + "i";
        }
        if (word == "y") return 1;

        // Remove h because it doesn't affect syllables (except ch, but vowels around it just don't combine if there's no h)
        word = word.Replace("h", "");

        // Vocalic group patterns:
        // Strong vowels: [aeoáéóíú] (í and ú act as strong to break diphthongs forming hiatus)
        // Weak vowels: [iuü]
        
        // A single nucleus can be:
        // 1. Triphthong: weak + strong + weak -> [iuü][aeoáéó][iuü]
        // 2. Diphthong: weak + strong -> [iuü][aeoáéó]
        // 3. Diphthong: strong + weak -> [aeoáéó][iuü]
        // 4. Diphthong: weak + weak -> [iuü][iuü] 
        // 5. Strong -> [aeoáéóíú]
        // 6. Weak -> [iuü]
        
        string pattern = "([iuü][aeoáéó][iuü])|([iuü][aeoáéó])|([aeoáéó][iuü])|([iuü][iuü])|([aeoáéóíú])|([iuü])";
        var matches = Regex.Matches(word, pattern, RegexOptions.IgnoreCase);
        
        return matches.Count == 0 ? 1 : matches.Count;
    }

    [GeneratedRegex(@"[^.!?]+[.!?]*")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"\b\p{L}+\b")]
    private static partial Regex WordRegex();
}
