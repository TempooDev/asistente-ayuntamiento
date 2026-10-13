namespace AsistenteAyuntamiento.Application.Common.VectorStore;

/// <summary>Nearest-neighbour query.</summary>
/// <param name="Vector">Query embedding.</param>
/// <param name="Limit">Maximum number of hits.</param>
/// <param name="ScoreThreshold">Minimum similarity; <c>null</c> returns the top <paramref name="Limit"/> hits.</param>
/// <param name="TextFilter">
/// Optional (field, text) filter: keeps points whose field matches the text <b>or has no value</b>, so documents that
/// were never tagged (for example, without municipality) are not excluded.
/// </param>
public sealed record VectorSearchRequest(
    float[] Vector,
    int Limit,
    float? ScoreThreshold = null,
    (string Field, string Text)? TextFilter = null);
