using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Metrics;
using AsistenteAyuntamiento.Application.Features.Metrics.DTOs;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Metrics;

public class AiMetricsQueryServiceTests
{
    private readonly string _databaseName = $"metrics-{Guid.NewGuid()}";

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

        var summary = await new AiMetricsQueryService(db).GetSummaryAsync();

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

        var summary = await new AiMetricsQueryService(tenant).GetSummaryAsync();

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

        var usage = await new AiMetricsQueryService(db).GetIngestionUsageAsync();

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

        var usage = await new AiMetricsQueryService(db).GetIngestionUsageAsync();

        usage.Should().Match<IngestionUsageDto>(u =>
            u.Documents == 0 && u.EnrichmentCalls == 0 && u.EmbeddedTokens == 0 && u.AverageProcessingDurationMs == 0);
    }

    [Fact]
    public async Task History_is_paged_newest_first_and_scoped_to_the_tenant()
    {
        await SeedAsync();
        await using var db = Context("org_A");

        var page = await new AiMetricsQueryService(db).GetHistoryAsync(page: 1, pageSize: 500);

        page.PageSize.Should().Be(AiMetricsQueryService.DefaultPageSize);
        page.TotalItems.Should().Be(3);
        page.Items.Should().OnlyContain(l => l.TenantId == "org_A");
    }
}
