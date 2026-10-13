using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Chat.Pipelines;

public static class RagPipelineExtensions
{
    public static IRagPipeline Get(this IEnumerable<IRagPipeline> pipelines, PipelineType type) =>
        pipelines.FirstOrDefault(p => p.Type == type)
        ?? throw new InvalidOperationException($"No RAG pipeline registered for {type}.");
}
