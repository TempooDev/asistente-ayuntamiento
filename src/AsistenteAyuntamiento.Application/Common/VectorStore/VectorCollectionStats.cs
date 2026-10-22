namespace AsistenteAyuntamiento.Application.Common.VectorStore;

/// <summary>Size of the vectors of a collection and how many points it holds.</summary>
public sealed record VectorCollectionStats(int VectorSize, long Points);
