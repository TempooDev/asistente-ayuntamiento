using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Worker;

/// <summary><c>WORKER_PIPELINE_MODE</c>: the pipeline whose queue this Worker consumes.</summary>
public static class WorkerPipelineMode
{
    /// <summary>
    /// <c>BASELINE</c>, <c>HIERARCHICAL</c>, <c>RECURSIVE</c> or <c>SEMANTIC</c>, case-insensitive. An unset value, a
    /// number or an unknown name means <c>BASELINE</c>.
    /// </summary>
    public static PipelineType Parse(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Trim().All(char.IsDigit)
        && Enum.TryParse<PipelineType>(value.Trim(), ignoreCase: true, out var pipeline)
        && Enum.IsDefined(pipeline)
            ? pipeline
            : PipelineType.Baseline;
}
