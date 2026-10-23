namespace AsistenteAyuntamiento.Domain.Common.Enums;

/// <summary>RAG variants. Recursive and semantic are flat-chunk variants of the baseline with another chunking strategy.</summary>
public enum PipelineType : byte
{
    Baseline = 1,
    Hierarchical = 2,
    Recursive = 3,
    Semantic = 4
}
