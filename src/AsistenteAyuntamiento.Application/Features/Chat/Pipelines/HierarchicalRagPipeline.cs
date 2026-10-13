using System.Runtime.CompilerServices;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Chat.Pipelines;

/// <summary>
/// Hierarchical RAG: query expansion, hybrid (dense + full-text) retrieval of child fragments with their parent
/// documents, and a plain-language answer from the platform model.
/// </summary>
public sealed class HierarchicalRagPipeline(
    IQueryExpansionService queryExpansionService,
    IHybridRetrievalService hybridRetrievalService,
    IClearLanguageGenerationService generationService) : IRagPipeline
{
    private const int RetrievalLimit = 5;

    public PipelineType Type => PipelineType.Hierarchical;

    public async IAsyncEnumerable<string> StreamAsync(ChatTurn turn, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var scope = new LlmCallScope(turn.TenantId, turn.TelemetryPipeline ?? LlmPipelines.Hierarchical) { UserId = turn.UserId };
        var expandedQuery = await queryExpansionService.ExpandQueryAsync(turn.UserQuery, scope, cancellationToken);
        var retrieved = await hybridRetrievalService.RetrieveAsync(expandedQuery, RetrievalLimit, cancellationToken);

        await foreach (var chunk in generationService.GenerateStreamingResponseAsync(turn.UserQuery, retrieved, scope, cancellationToken))
        {
            if (chunk.Content is not null)
            {
                yield return chunk.Content;
            }
        }
    }
}
