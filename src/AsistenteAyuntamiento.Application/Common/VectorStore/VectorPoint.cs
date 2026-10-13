namespace AsistenteAyuntamiento.Application.Common.VectorStore;

/// <summary>A vector to store. Payload values must be <see cref="string"/>, <see cref="long"/>, <see cref="int"/>, <see cref="double"/> or <see cref="bool"/>.</summary>
public sealed record VectorPoint(long Id, float[] Vector, IReadOnlyDictionary<string, object> Payload);
