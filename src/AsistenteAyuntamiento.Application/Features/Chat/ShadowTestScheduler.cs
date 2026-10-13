using System.Diagnostics;
using System.Text;
using AsistenteAyuntamiento.Application.Common.Background;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Arena;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>Runs shadow tests on <see cref="IBackgroundWorkQueue"/> (tracked, tenant-scoped, cancelled on shutdown).</summary>
public sealed class ShadowTestScheduler(IBackgroundWorkQueue queue, ILogger<ShadowTestScheduler> logger) : IShadowTestScheduler
{
    /// <summary>Share of streamed answers that get a shadow run.</summary>
    internal const double SampleRate = 0.40;

    public bool ShouldRun() => Random.Shared.NextDouble() < SampleRate;

    public void Schedule(ChatTurn turn, PipelineType answeredBy, string answer, long answerLatencyMs)
    {
        var shadowPipeline = answeredBy == PipelineType.Baseline ? PipelineType.Hierarchical : PipelineType.Baseline;
        var answeredIsLeft = Random.Shared.NextDouble() < 0.5;

        var item = BackgroundWorkItem.ForTenant("shadow-test", turn.TenantId, async (services, stoppingToken) =>
        {
            var pipeline = services.GetServices<IRagPipeline>().Get(shadowPipeline);
            var stopwatch = Stopwatch.StartNew();
            var shadowAnswer = new StringBuilder();
            await foreach (var chunk in pipeline.StreamAsync(turn with { TelemetryPipeline = LlmPipelines.Shadow }, stoppingToken))
            {
                shadowAnswer.Append(chunk);
            }
            stopwatch.Stop();

            var answered = new Side(answeredBy, answer, answerLatencyMs);
            var shadow = new Side(shadowPipeline, shadowAnswer.ToString(), stopwatch.ElapsedMilliseconds);
            var (left, right) = answeredIsLeft ? (answered, shadow) : (shadow, answered);

            var db = services.GetRequiredService<IAppDbContext>();
            db.ArenaBattles.Add(new ArenaBattle
            {
                TenantId = turn.TenantId,
                UserId = turn.UserId,
                UserQuery = turn.UserQuery,
                CreatedAt = DateTime.UtcNow,
                LeftSystem = left.Pipeline,
                RightSystem = right.Pipeline,
                LeftResponse = left.Answer,
                RightResponse = right.Answer,
                LeftLatencyMs = (int)left.LatencyMs,
                RightLatencyMs = (int)right.LatencyMs,
                LeftTokens = TokenUsageInfo.Estimate(left.Answer),
                RightTokens = TokenUsageInfo.Estimate(right.Answer),
                Winner = BattleWinner.Pending,
                TraceId = Activity.Current?.TraceId.ToHexString(),
            });
            await db.SaveChangesAsync(stoppingToken);
        });

        if (!queue.TryEnqueue(item))
        {
            logger.LogWarning("Background queue full; shadow test skipped for tenant {TenantId}", turn.TenantId);
        }
    }

    private sealed record Side(PipelineType Pipeline, string Answer, long LatencyMs);
}
