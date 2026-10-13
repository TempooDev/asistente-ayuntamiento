using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AsistenteAyuntamiento.Application.Features.Chat.DTOs;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// Entry point for chat answers (used by <c>ChatHub</c>). Delegates to the RAG pipelines, the Arena and shadow
/// testing; it holds no model, retrieval or persistence logic itself.
/// </summary>
public sealed class AiChatService(
    BaselineRagPipeline baselinePipeline,
    IEnumerable<IRagPipeline> pipelines,
    IWinningPipelineSelector winningPipelineSelector,
    IShadowTestScheduler shadowTestScheduler,
    IArenaStreamingService arenaStreamingService) : IAiChatService
{
    /// <summary>
    /// Non-streaming answer from the baseline pipeline. Never throws for model errors: they are returned as a
    /// failed <see cref="AiCompletionResult"/> with a typed error code.
    /// </summary>
    public Task<AiCompletionResult> GetCompletionAsync(ChatHistory history, string tenantId, string userId, CancellationToken cancellationToken = default) =>
        baselinePipeline.CompleteAsync(new ChatTurn(history, tenantId, userId), cancellationToken);

    /// <summary>
    /// Streams the answer of the pipeline currently winning the Arena. A sample of answers also gets a shadow run
    /// of the other pipeline in the background (see <see cref="IShadowTestScheduler"/>). A model failure before any
    /// text becomes the typed <c>[LLM_*]</c> message as the answer; after some text it is thrown
    /// (<see cref="LlmStreamInterruptedException"/>).
    /// </summary>
    public async IAsyncEnumerable<string> GetStreamingCompletionAsync(
        ChatHistory history,
        string tenantId,
        string userId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var winner = await winningPipelineSelector.GetWinnerAsync(cancellationToken);
        var turn = new ChatTurn(history, tenantId, userId);

        // Copy before the winner rewrites the history with its own RAG context.
        var shadowTurn = shadowTestScheduler.ShouldRun() ? turn.Copy() : null;
        var answer = shadowTurn is null ? null : new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        var started = false;

        await using var enumerator = pipelines.Get(winner).StreamAsync(turn, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            string? failure = null;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }
            }
            catch (LlmStreamInterruptedException ex) when (!started)
            {
                failure = ex.Message;
            }

            if (failure is not null)
            {
                // Nothing to keep or compare: the chat shows the typed error as the answer, without a shadow test.
                yield return failure;
                yield break;
            }

            started = true;
            answer?.Append(enumerator.Current);
            yield return enumerator.Current;
        }

        if (shadowTurn is not null)
        {
            shadowTestScheduler.Schedule(shadowTurn, winner, answer!.ToString(), stopwatch.ElapsedMilliseconds);
        }
    }

    public IAsyncEnumerable<ArenaStreamChunk> GetArenaStreamingCompletionAsync(
        ChatHistory history,
        string tenantId,
        string userId,
        CancellationToken cancellationToken = default) =>
        arenaStreamingService.StreamAsync(new ChatTurn(history, tenantId, userId), cancellationToken);
}
