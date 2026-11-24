using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Features.Chat;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Chat;

/// <summary>Retention deletes only the sessions inactive for longer than the period, in every tenant.</summary>
public sealed class ChatRetentionWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly string _databaseName = $"retention-{Guid.NewGuid()}";

    private AppDbContext NewContext(string tenant = "org_a") => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == tenant));

    private ChatRetentionWorker CreateWorker()
    {
        var services = new ServiceCollection();
        services.AddScoped<IAppDbContext>(_ => NewContext());
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(Now);
        return new ChatRetentionWorker(
            services.BuildServiceProvider(),
            new ConfigurationBuilder().Build(),
            time.Object,
            NullLogger<ChatRetentionWorker>.Instance);
    }

    private static ChatSession Session(string tenant, int createdDaysAgo, params int[] messageDaysAgo)
    {
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = "auth0|u1",
            TenantId = tenant,
            CreatedAt = Now.UtcDateTime.AddDays(-createdDaysAgo),
        };
        foreach (var days in messageDaysAgo)
        {
            session.Messages.Add(new ChatMessage
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                Role = "user",
                Content = "¿Plazo?",
                CreatedAt = Now.UtcDateTime.AddDays(-days),
            });
        }
        return session;
    }

    [Fact]
    public async Task Deletes_inactive_sessions_of_every_tenant_and_keeps_recent_ones()
    {
        var old = Session("org_a", 40, 40, 35);
        var oldOtherTenant = Session("org_b", 50, 45);
        var oldButActive = Session("org_a", 60, 60, 2);
        var recent = Session("org_b", 3, 3);
        await using (var db = NewContext())
        {
            db.ChatSessions.AddRange(old, oldOtherTenant, oldButActive, recent);
            await db.SaveChangesAsync();
        }

        var deleted = await CreateWorker().PurgeAsync(30, CancellationToken.None);

        deleted.Should().Be(2);
        await using var check = NewContext();
        var remaining = await check.ChatSessions.IgnoreQueryFilters().Select(s => s.Id).ToListAsync();
        remaining.Should().BeEquivalentTo([oldButActive.Id, recent.Id]);
        (await check.ChatMessages.IgnoreQueryFilters().CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Does_nothing_when_no_session_has_expired()
    {
        await using (var db = NewContext())
        {
            db.ChatSessions.Add(Session("org_a", 5, 5));
            await db.SaveChangesAsync();
        }

        (await CreateWorker().PurgeAsync(30, CancellationToken.None)).Should().Be(0);
    }
}
