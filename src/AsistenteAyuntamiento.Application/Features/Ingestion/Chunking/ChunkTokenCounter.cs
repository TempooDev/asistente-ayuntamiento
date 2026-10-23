namespace AsistenteAyuntamiento.Application.Features.Ingestion.Chunking;

/// <summary>
/// Token estimate shared by every chunking strategy and the ingestion metrics: the same as Semantic Kernel's
/// <c>TextChunker</c> default (four characters per token), so chunk sizes are comparable across strategies.
/// </summary>
public static class ChunkTokenCounter
{
    public static int Count(string text) => text.Length >> 2;

    /// <summary>Most characters a text can have and still count as at most <paramref name="tokens"/>.</summary>
    public static int MaxCharacters(int tokens) => (tokens << 2) + 3;
}
