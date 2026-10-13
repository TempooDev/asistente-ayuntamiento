using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Arena;
using AsistenteAyuntamiento.Application.Features.Arena.Models;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Arena;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Tests.Features.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using Testcontainers.PostgreSql;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Arena;

/// <summary>
/// Two votes on the same battle that both read it as pending (real PostgreSQL, xmin row version): only the first is
/// saved, the second is rejected as already voted.
/// </summary>
public sealed class ArenaVoteConcurrencyTests : IAsyncLifetime
{
    private const string Tenant = "org_A";
    private const string User = "auth0|ana";
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.IsDockerAvailable) return;
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres!.GetConnectionString(), o => o.UseVector()).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == Tenant));

    private static ArenaService Service(AppDbContext db)
    {
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(Mock.Of<IChatCompletionService>());
        builder.Services.AddSingleton(Mock.Of<IEmbeddingGenerator<string, Embedding<float>>>());
        return new ArenaService(db, Mock.Of<IServiceScopeFactory>(), builder.Build(), Mock.Of<IVectorStore>(),
            new ConfigurationBuilder().Build(), Mock.Of<ILlmUsageMetrics>(), Mock.Of<IAiCallRecorder>(), NullLogger<ArenaService>.Instance);
    }

    [DockerFact]
    public async Task A_vote_that_read_the_battle_before_another_vote_was_saved_is_rejected()
    {
        var sessionId = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.ArenaBattles.Add(new ArenaBattle
            {
                SessionId = sessionId,
                TenantId = Tenant,
                UserId = User,
                UserQuery = "¿Plazos?",
                LeftSystem = PipelineType.Baseline,
                RightSystem = PipelineType.Hierarchical,
                LeftResponse = "A",
                RightResponse = "B",
                Winner = BattleWinner.Pending
            });
            await seed.SaveChangesAsync();
        }

        await using var first = NewContext();
        await using var second = NewContext();
        // The second request reads the battle while it is still pending...
        (await second.ArenaBattles.SingleAsync(b => b.SessionId == sessionId)).Winner.Should().Be(BattleWinner.Pending);

        // ...then the first vote is saved...
        await Service(first).VoteAsync(new ArenaVoteRequest { SessionId = sessionId, Winner = "ALFA" }, User);

        // ...so the second one must fail instead of overwriting it.
        var secondVote = () => Service(second).VoteAsync(new ArenaVoteRequest { SessionId = sessionId, Winner = "BETA" }, User);
        await secondVote.Should().ThrowAsync<ArenaVoteConflictException>();

        await using var check = NewContext();
        (await check.ArenaBattles.AsNoTracking().SingleAsync(b => b.SessionId == sessionId)).Winner.Should().Be(BattleWinner.Alfa);
    }
}
