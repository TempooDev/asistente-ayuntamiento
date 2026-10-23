namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

/// <summary>
/// Splits Spanish bulletin text into sentences. A sentence ends at <c>.</c>, <c>!</c>, <c>?</c> or <c>…</c> followed
/// by whitespace and a character that is not a lowercase letter, or at a blank line. A period after a common
/// abbreviation (<c>art.</c>, <c>núm.</c>, <c>Sr.</c>, <c>Dña.</c>…), an initial or a token with inner periods
/// (<c>S.A.</c>) is not an end; ordinals such as <c>1.º</c> never are, since no whitespace follows the period.
/// </summary>
public static class SpanishSentenceSplitter
{
    /// <summary>Lowercase, without the final period.</summary>
    internal static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "art", "arts", "núm", "nums", "núms", "apdo", "apdos", "apart", "párr", "pág", "págs", "cap", "tít", "disp",
        "adic", "trans", "transit", "ap", "sr", "sra", "srta", "sres", "sras", "srs", "d", "dña", "dª",
        "dr", "dra", "excmo", "excma", "ilmo", "ilma", "iltmo", "iltma", "admón", "avda", "av", "c", "pza", "etc", "ej",
        "p", "pp", "vol", "ed", "aprox", "tel", "fdo", "vid", "cfr", "op", "cit", "ss", "sig", "sigs", "ntra", "ntro",
        "sta", "sto", "dcha", "izq", "izda", "prov", "dpto", "depto", "gral", "ref", "reg",
    };

    public static IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = text.Replace("\r\n", "\n");
        var sentences = new List<string>();
        var start = 0;

        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (c == '\n' && i + 1 < normalized.Length && normalized[i + 1] == '\n')
            {
                Add(normalized[start..i]);
                start = i + 2;
                i++;
                continue;
            }

            if (c is not ('.' or '!' or '?' or '…'))
            {
                continue;
            }

            // Closing quotes or brackets right after the mark belong to the sentence.
            var end = i + 1;
            while (end < normalized.Length && normalized[end] is '"' or '»' or '”' or ')' or '\'')
            {
                end++;
            }

            if (end >= normalized.Length || !char.IsWhiteSpace(normalized[end]))
            {
                continue;
            }

            var next = end;
            while (next < normalized.Length && char.IsWhiteSpace(normalized[next]))
            {
                next++;
            }

            if (next < normalized.Length && char.IsLower(normalized[next]))
            {
                continue;
            }

            if (c == '.' && IsAbbreviation(normalized, start, i))
            {
                continue;
            }

            Add(normalized[start..end]);
            start = end;
            i = end - 1;
        }

        Add(normalized[start..]);
        return sentences;

        void Add(string sentence)
        {
            var trimmed = sentence.Trim();
            if (trimmed.Length > 0)
            {
                sentences.Add(trimmed);
            }
        }
    }

    /// <summary>Whether the word ending at the period in <paramref name="period"/> is an abbreviation or an initial.</summary>
    private static bool IsAbbreviation(string text, int sentenceStart, int period)
    {
        var wordStart = period;
        while (wordStart > sentenceStart && !char.IsWhiteSpace(text[wordStart - 1]) && text[wordStart - 1] is not ('(' or '"' or '«' or '“'))
        {
            wordStart--;
        }

        var word = text[wordStart..period];
        if (word.Length == 0)
        {
            return false;
        }

        // Initials (J. García) and acronyms with inner periods (S.A., B.O.E.), but not numbers (3.000).
        if ((word.Length == 1 && char.IsLetter(word[0])) || (word.Contains('.') && !word.Any(char.IsDigit)))
        {
            return true;
        }

        return Abbreviations.Contains(word);
    }
}
