using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Metrics;
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
        db.IngestionMetrics.Add(new IngestionMetric { DocumentId = "BOE-1", TotalLlmCalls = 4, TotalTokensEmbedded = 100, TotalLlmTokens = 40, ProcessingDurationMs = 2000 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Summary_aggregates_the_tenants_chat_calls_and_platform_ingestion()
    {
        await SeedAsync();
        await using var db = Context("org_A");

        var summary = await new AiMetricsQueryService(db).GetSummaryAsync();

        summary.TotalCalls.Should().Be(3 + 4);
        summary.SucceededCalls.Should().Be(2 + 4);
        summary.FailedCalls.Should().Be(1);
        summary.TotalInputTokens.Should().Be(11 + 100);
        summary.TotalOutputTokens.Should().Be(21 + 40);
        summary.Consumptions.Select(c => (c.ModelId, c.TotalCalls, c.TotalTokens)).Should().Equal(
            ("Ingestion Worker", 4, 140L),
            ("gpt", 2, 30L),
            ("gemini", 1, 2L));
        summary.Consumptions.Single(c => c.ModelId == "gpt").AverageDurationMs.Should().Be(200);
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
