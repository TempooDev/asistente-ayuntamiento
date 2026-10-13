using System.Globalization;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>
/// Parses the official publication date written by the Go scraper in <c>metadata.fecha_publicacion</c>:
/// <c>yyyyMMdd</c> for BOE (as in the BOE XML), <c>yyyy-MM-dd</c> for BOJA, or ISO 8601.
/// </summary>
public static class PublicationDateParser
{
    private static readonly string[] Formats = ["yyyyMMdd", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "O"];

    public static bool TryParse(string? value, out DateTime publicationDateUtc)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTime.TryParseExact(value.Trim(), Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            publicationDateUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        publicationDateUtc = default;
        return false;
    }
}
