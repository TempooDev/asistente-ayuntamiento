using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat.DTOs;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Arena;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Application.Features.Chat;

public sealed class ArenaStreamingService(
    IServiceScopeFactory scopeFactory,
    IAppDbContext dbContext,
    ILogger<ArenaStreamingService> logger) : IArenaStreamingService
{
    public const string SessionIdOption = "SessionId";
    public const string AlfaOption = "Alfa";
    public const string BetaOption = "Beta";

    public async IAsyncEnumerable<ArenaStreamChunk> StreamAsync(ChatTurn turn, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();
        var hierarchicalIsAlfa = Random.Shared.NextDouble() > 0.5;

        // The battle id goes first so the client can vote on it.
        yield return new ArenaStreamChunk(SessionIdOption, sessionId.ToString());

        var alfa = new ArenaSide(AlfaOption, hierarchicalIsAlfa ? PipelineType.Hierarchical : PipelineType.Baseline);
        var beta = new ArenaSide(BetaOption, hierarchicalIsAlfa ? PipelineType.Baseline : PipelineType.Hierarchical);

        // Each side runs concurrently in its own DI scope (DbContext and kernels are not thread-safe).
        var runs = Task.WhenAll(RunSideAsync(alfa, turn.Copy(), cancellationToken), RunSideAsync(beta, turn.Copy(), cancellationToken));

        // Forward chunks from whichever side produces them first.
        var waiting = new Dictionary<Task<bool>, ArenaSide>
        {
            [alfa.Output.Reader.WaitToReadAsync(cancellationToken).AsTask()] = alfa,
            [beta.Output.Reader.WaitToReadAsync(cancellationToken).AsTask()] = beta,
        };
        while (waiting.Count > 0)
        {
            var ready = await Task.WhenAny(waiting.Keys);
            var side = waiting[ready];
            waiting.Remove(ready);

            if (!await ready)
            {
                continue; // This side has finished.
            }

            while (side.Output.Reader.TryRead(out var text))
            {
                side.Answer.Append(text);
                yield return new ArenaStreamChunk(side.Option, text);
            }
            waiting[side.Output.Reader.WaitToReadAsync(cancellationToken).AsTask()] = side;
        }

        await runs;
        await SaveBattleAsync(sessionId, turn, alfa, beta, cancellationToken);
    }

    private async Task RunSideAsync(ArenaSide side, ChatTurn turn, CancellationToken cancellationToken)
    {
        // Yield first so both sides start without waiting for each other's synchronous setup.
        await Task.Yield();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var pipeline = scope.ServiceProvider.GetServices<IRagPipeline>().Get(side.Pipeline);
            await foreach (var chunk in pipeline.StreamAsync(turn, cancellationToken))
            {
                await side.Output.Writer.WriteAsync(chunk, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client left; the other side is cancelled too.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Arena side {Option} ({Pipeline}) failed", side.Option, side.Pipeline);
        }
        finally
        {
            side.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
            side.Output.Writer.Complete();
        }
    }

    private async Task SaveBattleAsync(Guid sessionId, ChatTurn turn, ArenaSide alfa, ArenaSide beta, CancellationToken cancellationToken)
    {
        var left = alfa.Answer.ToString();
        var right = beta.Answer.ToString();
        dbContext.ArenaBattles.Add(new ArenaBattle
        {
            SessionId = sessionId,
            TenantId = turn.TenantId,
            UserId = turn.UserId,
            UserQuery = turn.UserQuery,
            CreatedAt = DateTime.UtcNow,
            LeftSystem = alfa.Pipeline,
            RightSystem = beta.Pipeline,
            LeftResponse = left,
            RightResponse = right,
            LeftLatencyMs = alfa.LatencyMs,
            RightLatencyMs = beta.LatencyMs,
            LeftTokens = TokenUsageInfo.Estimate(left),
            RightTokens = TokenUsageInfo.Estimate(right),
            Winner = BattleWinner.Pending
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("ArenaBattle insert returned 0 rows due to cancellation.");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("ArenaBattle insert cancelled by user.");
        }
    }

    private sealed class ArenaSide(string option, PipelineType pipeline)
    {
        public string Option { get; } = option;
        public PipelineType Pipeline { get; } = pipeline;
        public Channel<string> Output { get; } = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        public StringBuilder Answer { get; } = new();
        public int LatencyMs { get; set; }
    }
}
