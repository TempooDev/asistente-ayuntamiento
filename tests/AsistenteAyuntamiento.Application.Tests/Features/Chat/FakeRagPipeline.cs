using System.Runtime.CompilerServices;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

/// <summary>Pipeline double: streams fixed chunks, optionally waiting for a signal before each one.</summary>
internal sealed class FakeRagPipeline(PipelineType type, params string[] chunks) : IRagPipeline
{
    public PipelineType Type => type;

    public List<ChatTurn> Turns { get; } = [];

    /// <summary>The chunks streamed before <see cref="Failure"/> (empty: the pipeline fails before any text).</summary>
    public string[] Chunks { get; set; } = chunks;

    /// <summary>When set, each chunk waits for this task (to control interleaving).</summary>
    public Task? Gate { get; set; }

    public async IAsyncEnumerable<string> StreamAsync(ChatTurn turn, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Turns.Add(turn);
        foreach (var chunk in Chunks)
        {
            if (Gate is not null)
            {
                await Gate.WaitAsync(cancellationToken);
            }
            await Task.Yield();
            yield return chunk;
        }

        if (Failure is not null)
        {
            throw Failure;
        }
    }

    /// <summary>When set, thrown after the last chunk.</summary>
    public Exception? Failure { get; set; }
}
