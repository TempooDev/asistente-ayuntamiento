namespace AsistenteAyuntamiento.Application.Common.Messaging;

/// <summary>Which ingestion pipelines a document is queued for.</summary>
[Flags]
public enum IngestionPipelines
{
    None = 0,
    Baseline = 1,
    Hierarchical = 2,
    Both = Baseline | Hierarchical,
}
