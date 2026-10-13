namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>A bulletin document cited in an answer.</summary>
public sealed record DocumentSource(
    string Title,
    string Department,
    string Date,
    string BlobPath);
