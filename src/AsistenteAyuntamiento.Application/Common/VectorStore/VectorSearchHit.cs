using System.Globalization;

namespace AsistenteAyuntamiento.Application.Common.VectorStore;

/// <summary>A search result: point id, similarity score and payload.</summary>
public sealed record VectorSearchHit(long Id, float Score, IReadOnlyDictionary<string, object?> Payload)
{
    /// <summary>Payload value as text; empty when the field is missing.</summary>
    public string GetString(string key) =>
        Payload.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;

    /// <summary>Payload value as an integer; 0 when the field is missing.</summary>
    public long GetInt64(string key) =>
        Payload.TryGetValue(key, out var value) && value is not null ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0;

    /// <summary>Payload value parsed as a round-trip (ISO 8601) date; <c>null</c> when missing or invalid.</summary>
    public DateTime? GetDate(string key) =>
        DateTime.TryParse(GetString(key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null;
}
