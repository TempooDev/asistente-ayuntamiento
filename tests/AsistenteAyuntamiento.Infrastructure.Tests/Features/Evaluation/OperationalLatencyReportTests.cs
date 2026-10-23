using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Arena;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Evaluation;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Tests.Features.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Evaluation;

/// <summary>One migrated PostgreSQL with the seed data, shared by the read-only tests of the report.</summary>
public sealed class LatencyReportDatabase : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public string ConnectionString => _postgres!.GetConnectionString();

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.IsDockerAvailable) return;
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await _postgres.StartAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString, o => o.UseVector()).Options,
            Mock.Of<ICurrentTenantService>(t => t.TenantId == "org_A"));
        await db.Database.MigrateAsync();

        var inPeriod = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 10; i++)
        {
            db.AiCallLogs.Add(new AiCallLog
            {
                TenantId = "org_A",
                UserId = "u",
                ModelId = "m",
                Success = true,
                DurationMs = i * 100,
                TimeToFirstTokenMs = i * 10,
                Pipeline = "baseline",
                CreatedAt = inPeriod
            });
        }
        db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", UserId = "u", ModelId = "m", Success = false, DurationMs = 99, Pipeline = "baseline", CreatedAt = inPeriod });
        db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", UserId = "u", ModelId = "m", Success = true, DurationMs = 99_999, Pipeline = "evaluation", CreatedAt = inPeriod });
        db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", UserId = "u", ModelId = "m", Success = true, DurationMs = 99_999, Pipeline = "shadow", CreatedAt = inPeriod });
        db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", UserId = "u", ModelId = "m", Success = false, DurationMs = 50, TimeToFirstTokenMs = 1, Pipeline = "baseline", CreatedAt = inPeriod });
        db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", UserId = "u", ModelId = "m", Success = true, DurationMs = 99_999, Pipeline = "baseline", CreatedAt = inPeriod.AddMonths(-2) });
        // Arena slots, merged into one "arena" row; a failed step that must not move the percentiles; a label with a comma.
        db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", UserId = "u", ModelId = "m", Success = true, DurationMs = 200, Pipeline = "arena_alfa", CreatedAt = inPeriod });
        db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", UserId = "u", ModelId = "m", Success = true, DurationMs = 400, Pipeline = "arena_beta", CreatedAt = inPeriod });
        db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", UserId = "u", ModelId = "m", Success = true, DurationMs = 300, Pipeline = "hierarchical", Operation = "query_expansion", CreatedAt = inPeriod });
        db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", UserId = "u", ModelId = "m", Success = false, DurationMs = 100_000, Pipeline = "hierarchical", Operation = "query_expansion", CreatedAt = inPeriod });
        db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", UserId = "u", ModelId = "m", Success = true, DurationMs = 10, Pipeline = "x,y", Operation = "a\"b", CreatedAt = inPeriod });
        // 00:30 on 1 December in Madrid is still November in UTC, but belongs to December: left out.
        db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", UserId = "u", ModelId = "m", Success = true, DurationMs = 99_999, Pipeline = "baseline", CreatedAt = new DateTime(2026, 11, 30, 23, 30, 0, DateTimeKind.Utc) });
        db.ArenaBattles.Add(new ArenaBattle
        {
            TenantId = "org_A",
            UserId = "u",
            UserQuery = "secreto",
            LeftSystem = PipelineType.Baseline,
            RightSystem = PipelineType.Hierarchical,
            LeftLatencyMs = 1000,
            RightLatencyMs = 3000,
            CreatedAt = inPeriod
        });
        db.IngestionMetrics.Add(new IngestionMetric { DocumentId = "BOE-1", Pipeline = PipelineType.Semantic, ProcessingDurationMs = 9000, CreatedAt = inPeriod });
        await db.SaveChangesAsync();
        // Reprocessed: only this latest run counts.
        db.IngestionMetrics.Add(new IngestionMetric { DocumentId = "BOE-1", Pipeline = PipelineType.Semantic, ProcessingDurationMs = 4000, CreatedAt = inPeriod });
        var state = new DocumentJobState { DocumentId = "BOE-1" };
        state.SetPipelineStatus(PipelineType.Baseline, JobStatus.Completed);
        state.SetPipelineStatus(PipelineType.Semantic, JobStatus.Failed, "boom");
        state.LastUpdatedAt = inPeriod;
        db.DocumentJobStates.Add(state);
        await db.SaveChangesAsync();
        // SetPipelineStatus stamps LastUpdatedAt with the current time: put it back in the period.
        await db.Database.ExecuteSqlRawAsync("UPDATE ingestion.\"DocumentJobStates\" SET \"LastUpdatedAt\" = '2026-11-10T12:00:00Z'");
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
    }
}

/// <summary>The operational latency report for chapter 8 (spec: rag-evaluation, Operational Latency Report), on PostgreSQL.</summary>
public sealed class OperationalLatencyReportTests(LatencyReportDatabase database) : IClassFixture<LatencyReportDatabase>, IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "latency-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private Task<IReadOnlyList<string>> Report() =>
        new OperationalLatencyReport(database.ConnectionString).WriteAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), _directory);

    private string[] Lines(string file) => File.ReadAllLines(Path.Combine(_directory, file));

    [DockerFact]
    public async Task Writes_one_csv_per_measure_with_percentiles_per_pipeline()
    {
        (await Report()).Select(Path.GetFileName).Should().BeEquivalentTo(OperationalLatencyReport.Measures.Keys);

        // 10 successful answers of 100..1000 ms and two failures (one with a first token that must not count); evaluation
        // and shadow calls and calls outside the period are left out.
        Lines("chat-answers.csv").Should().Equal(
            "pipeline,answers,failed_share,mean_ms,p50_ms,p95_ms,ttft_n,ttft_p50_ms,ttft_p95_ms",
            "arena,2,0.0000,300.0,300.0,390.0,0,,",
            "baseline,12,0.1667,550.0,550.0,955.0,10,55.0,95.5");
        // Failures count in the share but not in the percentiles; free-text labels are quoted.
        Lines("llm-calls.csv").Should().Contain(["hierarchical,query_expansion,2,0.5000,300.0,300.0", "\"x,y\",\"a\"\"b\",1,0.0000,10.0,10.0"]);
        Lines("arena-sides.csv").Skip(1).Should().Equal("Baseline,1,1000.0,1000.0,1000.0", "Hierarchical,1,3000.0,3000.0,3000.0");
        Lines("ingestion.csv").Skip(1).Should().Equal("Semantic,2,1,4000.0,4000.0,4000.0");
        Lines("ingestion-failures.csv").Skip(1).Should().Contain(["Baseline,1,0,0.0000", "Semantic,1,1,1.0000"]);
    }

    [DockerFact]
    public async Task Never_writes_question_or_answer_text_nor_identifiers()
    {
        await Report();

        var all = string.Concat(OperationalLatencyReport.Measures.Keys.Select(f => File.ReadAllText(Path.Combine(_directory, f))));
        all.Should().NotContain("secreto").And.NotContain("org_A").And.NotContain("BOE-1");
    }
}
