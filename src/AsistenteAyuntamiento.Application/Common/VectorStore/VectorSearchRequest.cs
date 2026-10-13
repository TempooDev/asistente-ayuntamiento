namespace AsistenteAyuntamiento.Application.Common.VectorStore;

/// <summary>Nearest-neighbour query.</summary>
/// <param name="Vector">Query embedding.</param>
/// <param name="Limit">Maximum number of hits.</param>
/// <param name="ScoreThreshold">Minimum similarity; <c>null</c> returns the top <paramref name="Limit"/> hits.</param>
/// <param name="TextFilter">Optional full-text match on a payload field (field, text).</param>
public sealed record VectorSearchRequest(
    float[] Vector,
    int Limit,
    float? ScoreThreshold = null,
    (string Field, string Text)? TextFilter = null);
