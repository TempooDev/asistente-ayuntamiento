namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class RawFragment
{
    /// <summary>Article (BOE) or section (BOJA) the fragment belongs to.</summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>Part of the section (e.g. <c>parte 2</c> of a split article); <c>null</c> when there is none.</summary>
    public string? SubSection { get; set; }

    public string OriginalText { get; set; } = string.Empty;

    /// <summary>Section plus sub-section, e.g. <c>a5 (parte 2)</c>; stored as the fragment's <c>SubSection</c>.</summary>
    public string Label => string.IsNullOrWhiteSpace(SubSection) ? Section : $"{Section} ({SubSection})";
}
