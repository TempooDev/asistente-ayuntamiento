using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Metrics;
using AsistenteAyuntamiento.Application.Features.Metrics.DTOs;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Metrics;

public class AiMetricsQueryServiceTests
{
    private readonly string _databaseName = $"metrics-{Guid.NewGuid()}";

    private static readonly MetricsPeriod Period = MetricsPeriod.Resolve(null, null, DateTime.UtcNow);

    private static AiMetricsQueryService Sut(AppDbContext db) => new(db, new LlmUsageMetrics(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Pricing:openai:gpt:Input"] = "1000000",
            ["Ai:Pricing:openai:gpt:Output"] = "2000000",
        }).Build(),
        NullLogger<LlmUsageMetrics>.Instance));

    private AppDbContext Context(string tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == tenant));

    private async Task SeedAsync()
    {
        await using var db = Context("seed");
        db.AiCallLogs.AddRange(
            new AiCallLog { TenantId = "org_A", ModelId = "gpt", Success = true, DurationMs = 100, InputTokens = 10, OutputTokens = 20, TotalTokens = 30 },
            new AiCallLog { TenantId = "org_A", ModelId = "gpt", Success = false, DurationMs = 300 },
            new AiCallLog { TenantId = "org_A", ModelId = "gemini", Success = true, DurationMs = 50, InputTokens = 1, OutputTokens = 1, TotalTokens = 2 },
            new AiCallLog { TenantId = "org_B", ModelId = "gpt", Success = true, DurationMs = 999, InputTokens = 500, OutputTokens = 500, TotalTokens = 1000 });
        db.IngestionMetrics.AddRange(
            new IngestionMetric { DocumentId = "BOE-1", TotalLlmCalls = 4, TotalTokensEmbedded = 100, TotalLlmTokens = 40, ProcessingDurationMs = 2000 },
            new IngestionMetric { DocumentId = "BOE-2", TotalLlmCalls = 0, TotalTokensEmbedded = 50, TotalLlmTokens = 0, ProcessingDurationMs = 1000 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Summary_aggregates_only_the_tenants_chat_calls()
    {
        await SeedAsync();
        await using var db = Context("org_A");

        var summary = await Sut(db).GetSummaryAsync(Period);

        summary.TotalCalls.Should().Be(3);
        summary.SucceededCalls.Should().Be(2);
        summary.FailedCalls.Should().Be(1);
        summary.SuccessRate.Should().BeApproximately(66.67, 0.01);
        summary.TotalInputTokens.Should().Be(11);
        summary.TotalOutputTokens.Should().Be(21);
        summary.Consumptions.Select(c => (c.ModelId, c.Type, c.TotalCalls, c.TotalTokens)).Should().Equal(
            ("gpt", "Respuesta", 2, 30L),
            ("gemini", "Respuesta", 1, 2L));
        summary.Consumptions.Single(c => c.ModelId == "gpt").AverageDurationMs.Should().Be(200);
    }

    [Fact]
    public async Task Other_steps_add_tokens_but_are_not_counted_as_calls()
    {
        await SeedAsync();
        await using (var db = Context("seed"))
        {
            db.AiCallLogs.AddRange(
                new AiCallLog { TenantId = "org_A", ModelId = "gpt", Operation = "query_expansion", Success = true, DurationMs = 5000, InputTokens = 100, OutputTokens = 10, TotalTokens = 110 },
                new AiCallLog { TenantId = "org_A", ModelId = "gpt", Operation = "history_analysis", Success = false, DurationMs = 9000 });
            await db.SaveChangesAsync();
        }
        await using var tenant = Context("org_A");

        var summary = await Sut(tenant).GetSummaryAsync(Period);

        (summary.TotalCalls, summary.SucceededCalls, summary.FailedCalls).Should().Be((3L, 2L, 1L));
        summary.AverageDurationMs.Should().Be(150, "only answers count: (100 + 300 + 50) / 3");
        summary.TotalTokens.Should().Be(32 + 110);
        summary.AverageTokensPerCall.Should().Be(16, "answer tokens (32) per successful answer (2)");
        summary.Consumptions.Should().Contain(c => c.ModelId == "gpt" && c.Type == "Expansión de consulta" && c.TotalTokens == 110)
            .And.Contain(c => c.ModelId == "gpt" && c.Type == "Análisis de historial" && c.TotalCalls == 1);
    }

    [Fact]
    public async Task Ingestion_usage_aggregates_every_ingested_document()
    {
        await SeedAsync();
        await using var db = Context("org_A");

        var usage = await Sut(db).GetIngestionUsageAsync(Period);

        usage.Documents.Should().Be(2);
        usage.EnrichmentCalls.Should().Be(4);
        usage.EnrichmentTokens.Should().Be(40);
        usage.EmbeddedTokens.Should().Be(150);
        usage.AverageProcessingDurationMs.Should().Be(1500);
    }

    [Fact]
    public async Task Ingestion_usage_is_zero_when_nothing_was_ingested()
    {
        await using var db = Context("org_A");

        var usage = await Sut(db).GetIngestionUsageAsync(Period);

        usage.Should().Match<IngestionUsageDto>(u =>
            u.Documents == 0 && u.EnrichmentCalls == 0 && u.EmbeddedTokens == 0 && u.AverageProcessingDurationMs == 0);
    }

    [Fact]
    public async Task History_is_paged_newest_first_and_scoped_to_the_tenant()
    {
        await SeedAsync();
        await using var db = Context("org_A");

        var page = await Sut(db).GetHistoryAsync(page: 1, pageSize: 500);

        page.PageSize.Should().Be(AiMetricsQueryService.DefaultPageSize);
        page.TotalItems.Should().Be(3);
        page.Items.Should().OnlyContain(l => l.TenantId == "org_A");
    }

    [Fact]
    public async Task Summary_reports_percentiles_ttft_and_leaves_out_evaluation_calls()
    {
        await using (var db = Context("seed"))
        {
            for (var i = 1; i <= 20; i++)
            {
                db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", ModelId = "llama", Provider = "ollama", Success = true, DurationMs = i * 100,
                    TimeToFirstTokenMs = i % 2 == 0 ? i * 10 : null, Pipeline = LlmPipelines.Baseline });
            }
            db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", ModelId = "llama", Success = true, DurationMs = 99_999, Pipeline = LlmPipelines.Evaluation });
            db.AiCallLogs.Add(new AiCallLog { TenantId = "org_A", ModelId = "llama", Success = true, DurationMs = 99_999, CreatedAt = DateTime.UtcNow.AddDays(-60) });
            await db.SaveChangesAsync();
        }
        await using var tenant = Context("org_A");

        var summary = await Sut(tenant).GetSummaryAsync(Period);

        summary.TotalCalls.Should().Be(20, "evaluation calls and calls before the period are left out");
        summary.LatencyP50Ms.Should().Be(1050);
        summary.LatencyP95Ms.Should().Be(1905);
        summary.TtftP50Ms.Should().Be(110);
        summary.EstimatedCostEur.Should().Be(0);
        summary.UnpricedModels.Should().BeEmpty();
        summary.Daily.Should().HaveCount(30);
        summary.Daily[^1].Answers.Should().Be(20);
        summary.Daily[0].LatencyP95Ms.Should().BeNull();
    }

    [Fact]
    public async Task Summary_prices_calls_lists_unpriced_models_and_splits_by_pipeline()
    {
        await using (var db = Context("seed"))
        {
            db.AiCallLogs.AddRange(
                new AiCallLog { TenantId = "org_A", ModelId = "gpt", Provider = "openai", Success = true, DurationMs = 100, InputTokens = 1, OutputTokens = 1, TotalTokens = 2, Pipeline = LlmPipelines.Baseline },
                new AiCallLog { TenantId = "org_A", ModelId = "gpt", Provider = "openai", Success = true, DurationMs = 300, InputTokens = 1, OutputTokens = 0, TotalTokens = 1, Pipeline = LlmPipelines.ArenaAlfa },
                new AiCallLog { TenantId = "org_A", ModelId = "gpt", Provider = "openai", Success = true, DurationMs = 500, Pipeline = LlmPipelines.ArenaBeta },
                new AiCallLog { TenantId = "org_A", ModelId = "mystery", Provider = "openrouter", Success = true, DurationMs = 100, InputTokens = 5, TotalTokens = 5, Pipeline = LlmPipelines.Hierarchical,
                    Operation = LlmOperations.QueryExpansion });
            await db.SaveChangesAsync();
        }
        await using var tenant = Context("org_A");

        var summary = await Sut(tenant).GetSummaryAsync(Period);

        summary.EstimatedCostEur.Should().Be(1 + 2 + 1, "gpt costs 1 € per input token and 2 € per output token in the test table");
        summary.UnpricedModels.Should().Equal("mystery");
        summary.CostByStep.Select(c => c.Step).Should().BeEquivalentTo("Respuesta", "Expansión de consulta");
        summary.Pipelines.Select(p => (p.Pipeline, p.Answers)).Should().BeEquivalentTo(new[] { ("arena", 2), ("baseline", 1) });
        summary.Pipelines.Single(p => p.Pipeline == "arena").CostPer1000AnswersEur.Should().Be(500);
    }

    [Fact]
    public async Task Ingestion_counts_the_latest_ingestion_of_each_document_per_pipeline()
    {
        await using (var db = Context("seed"))
        {
            db.IngestionMetrics.AddRange(
                new IngestionMetric { DocumentId = "BOE-1", Pipeline = PipelineType.Baseline, ProcessingDurationMs = 9000, ChunksGenerated = 9, CreatedAt = DateTime.UtcNow.AddDays(-2) },
                new IngestionMetric { DocumentId = "BOE-1", Pipeline = PipelineType.Baseline, ProcessingDurationMs = 1000, ChunksGenerated = 2 },
                new IngestionMetric { DocumentId = "BOE-1", Pipeline = PipelineType.Hierarchical, ProcessingDurationMs = 4000, ChunksGenerated = 6, TotalLlmCalls = 6 });
            db.DocumentJobStates.Add(new DocumentJobState { DocumentId = "BOE-2", BaselineStatus = JobStatus.Completed, HierarchicalStatus = JobStatus.Failed });
            await db.SaveChangesAsync();
        }
        await using var tenant = Context("org_A");

        var usage = await Sut(tenant).GetIngestionUsageAsync(Period);

        usage.Documents.Should().Be(2, "one row per document and pipeline");
        var baseline = usage.Pipelines.Single(p => p.Pipeline == "Baseline");
        (baseline.Documents, baseline.ProcessingP50Ms, baseline.AverageChunks, baseline.FailedDocuments).Should().Be((1L, 1000d, 2d, 0));
        var hierarchical = usage.Pipelines.Single(p => p.Pipeline == "Hierarchical");
        (hierarchical.Documents, hierarchical.EnrichmentCalls, hierarchical.FailedDocuments).Should().Be((1L, 6L, 1));
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-01")]
    [InlineData("2025-01-01", "2026-10-01")]
    public void Invalid_periods_are_refused(string from, string to) =>
        FluentActions.Invoking(() => MetricsPeriod.Resolve(DateOnly.Parse(from), DateOnly.Parse(to), DateTime.UtcNow)).Should().Throw<ArgumentException>();

    [Fact]
    public void Percentiles_interpolate_like_percentile_cont()
    {
        Percentiles.Of([1d, 2d, 3d, 4d], 0.5).Should().Be(2.5);
        Percentiles.Of([10d], 0.95).Should().Be(10);
        Percentiles.Of(Array.Empty<double>(), 0.5).Should().BeNull();
    }
}
