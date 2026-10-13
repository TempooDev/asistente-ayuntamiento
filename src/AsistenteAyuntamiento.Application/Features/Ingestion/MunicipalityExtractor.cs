using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>
/// Finds the municipality of a bulletin document from its issuing body (<c>metadata.departamento</c>) or, when that is
/// not an Ayuntamiento (BOJA always writes "Junta de Andalucía"), from the issuer clause of its title. Only documents
/// issued by a single Ayuntamiento get one: anything else (state and regional bodies, Diputación Provincial,
/// Mancomunidad, a ministry publishing an agreement with an Ayuntamiento...) returns <c>null</c>, because a document
/// without municipality applies to every municipality while a wrong one would hide it from the others. No LLM.
/// </summary>
public static partial class MunicipalityExtractor
{
    private const int MaxWords = 6;
    private const int MaxLength = 100; // ParentDocument.Municipality and ChildFragment.Municipality columns

    private static readonly HashSet<string> Connectors = new(StringComparer.Ordinal) { "de", "del", "la", "las", "los", "el", "y", "e", "i" };

    /// <summary>
    /// Display name of the municipality (title case, accents kept, connectors such as "de la" in lower case, province
    /// in parentheses removed), e.g. "Alhaurín de la Torre"; <c>null</c> when the document is not clearly from one.
    /// </summary>
    public static string? Extract(string? title, string? department) =>
        FromIssuer(department) ?? FromTitle(title);

    /// <summary>
    /// Key the municipality filter compares: lower case, without accents, punctuation as spaces and without a leading
    /// "Ayuntamiento de" or a province in parentheses ("Vélez-Málaga" and "AYUNTAMIENTO DE VELEZ MALAGA (Málaga)" both
    /// give "velez malaga"). Fragments and vector payloads store it; the search applies it to <c>filtro_municipio</c>
    /// and matches by substring, so case and accents never decide a match. <c>null</c> when nothing is left.
    /// </summary>
    public static string? ToMatchKey(string? municipality)
    {
        if (string.IsNullOrWhiteSpace(municipality))
        {
            return null;
        }

        var decomposed = Parenthesized().Replace(municipality, " ").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        var key = KeyPrefix().Replace(Spaces().Replace(builder.ToString(), " ").Trim(), "");
        return key.Length == 0 ? null : key;
    }

    private static string? FromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // "Anuncio de 3 de febrero de 2026, del Ayuntamiento de Sedella, de ..." or "Anuncio del Ayuntamiento de Ronda por ...".
        var match = DatedIssuer().Match(title);
        if (!match.Success) match = UndatedIssuer().Match(title);
        return match.Success ? FromIssuer(match.Groups["issuer"].Value) : null;
    }

    private static string? FromIssuer(string? issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            return null;
        }

        var match = AyuntamientoIssuer().Match(Spaces().Replace(issuer, " ").Trim());
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim();
        if (match.Groups["article"].Value.Equals("del", StringComparison.OrdinalIgnoreCase))
        {
            name = "El " + name; // "Ayuntamiento del Puerto de Santa María" -> "El Puerto de Santa María"
        }

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words.Length > MaxWords || name.Length > MaxLength || name.Any(char.IsDigit))
        {
            return null;
        }

        return string.Join(' ', words.Select((word, i) => Canonical(word, first: i == 0)));
    }

    private static string Canonical(string word, bool first)
    {
        var lower = word.ToLowerInvariant();
        if (!first && Connectors.Contains(lower))
        {
            return lower;
        }

        // "d'alacant" -> "d'Alacant" (or "D'..." first); hyphenated parts are capitalized: "Vélez-Málaga".
        var apostrophe = lower.IndexOfAny(['\'', '’']);
        if (apostrophe == 1 && lower.Length > 2)
        {
            var prefix = first ? char.ToUpperInvariant(lower[0]).ToString() : lower[0].ToString();
            return prefix + lower[1] + Capitalize(lower[2..]);
        }

        return Capitalize(lower);
    }

    private static string Capitalize(string lower) =>
        string.Join('-', lower.Split('-').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>
    /// The whole issuer is an Ayuntamiento: optional honorific, "Ayuntamiento de|del|d'" and a name, followed only by a
    /// province in parentheses and, in titles without a comma after the issuer, the start of the subject ("por el que...").
    /// </summary>
    [GeneratedRegex(
        @"^(?:(?:el|la)\s+)?(?:(?:excmo|excma|ilmo|ilma)\.?\s+|(?:excelent[íi]simo|ilustr[íi]simo|ilustre|muy\s+ilustre)\s+)?" +
        @"ayuntamiento\s+(?:(?<article>del)\s+|de\s+|d['’])(?<name>[^,;()]+?)\s*(?:\([^)]*\))?\s*" +
        @"(?:\s(?:por|sobre|relativ\w*|referente|para|mediante|en\s+relaci[óo]n|en\s+materia)\b.*)?\.?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AyuntamientoIssuer();

    /// <summary>The issuer clause right after the date of the title: "..., de 3 de febrero de 2026, del &lt;issuer&gt;, ...".</summary>
    [GeneratedRegex(@"\b\d{1,2}\s+de\s+\p{L}+\s+de\s+\d{4}\s*,\s*(?:del\s+|de\s+la\s+|de\s+los\s+|de\s+las\s+|de\s+)?(?<issuer>[^,]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatedIssuer();

    /// <summary>Titles without a date (BOE section V anuncios): "Anuncio del Ayuntamiento de Ronda por el que ...".</summary>
    [GeneratedRegex(@"(?:^|\s-\s)\s*(?:anuncio|edicto|resoluci[óo]n|acuerdo|decreto|bando)\s+(?:del\s+|de\s+la\s+)(?<issuer>[^,]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UndatedIssuer();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthesized();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(?:ayuntamiento|municipio)\s+(?:del?\s+|d\s+)?")]
    private static partial Regex KeyPrefix();
}
