using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Features.Chat;
using AsistenteAyuntamiento.Infrastructure.Tests.Features.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.PostgreSql;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Chat;

/// <summary>
/// Chat persistence against a real PostgreSQL: a bad message does not take the batch down, persistent failures
/// end in the dead-letter table, and an unreachable database keeps the messages buffered.
/// </summary>
public sealed class ChatPersistenceWorkerTests : IAsyncLifetime
{
    private const string Tenant = "org_chat";
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.IsDockerAvailable) return;
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await _postgres.StartAsync();
        await using var db = NewContext(_postgres.GetConnectionString());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private static AppDbContext NewContext(string connectionString) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString, o => o.UseVector()).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == Tenant));

    private static (ChatPersistenceWorker Worker, ChatMessageBuffer Buffer) CreateWorker(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAppDbContext>(_ => NewContext(connectionString));
        var buffer = new ChatMessageBuffer();
        return (new ChatPersistenceWorker(buffer, services.BuildServiceProvider(), NullLogger<ChatPersistenceWorker>.Instance), buffer);
    }

    private static ChatMessage Message(Guid sessionId, string content) =>
        new() { Id = Guid.NewGuid(), SessionId = sessionId, Role = "user", Content = content, CreatedAt = DateTime.UtcNow };

    [DockerFact]
    public async Task A_message_of_a_missing_session_is_dead_lettered_without_losing_the_rest_of_the_batch()
    {
        var sessionId = Guid.NewGuid();
        await using (var db = NewContext(_postgres!.GetConnectionString()))
        {
            db.ChatSessions.Add(new ChatSession { Id = sessionId, UserId = "auth0|u1", TenantId = Tenant });
            await db.SaveChangesAsync();
        }
        var (worker, buffer) = CreateWorker(_postgres.GetConnectionString());
        var good = Message(sessionId, "¿Plazo de alegaciones?");
        var orphan = Message(Guid.NewGuid(), "Mensaje de una sesión borrada");
        buffer.Enqueue(good);
        buffer.Enqueue(orphan);

        for (var i = 0; i < ChatPersistenceWorker.MaxAttempts; i++)
        {
            await worker.FlushAsync(CancellationToken.None);
        }

        buffer.PendingCount.Should().Be(0);
        await using var check = NewContext(_postgres.GetConnectionString());
        (await check.ChatMessages.Select(m => m.Id).ToListAsync()).Should().Equal(good.Id);
        var deadLetter = await check.FailedChatMessages.SingleAsync();
        deadLetter.Id.Should().Be(orphan.Id);
        deadLetter.Content.Should().Be(orphan.Content);
        deadLetter.Attempts.Should().Be(ChatPersistenceWorker.MaxAttempts);
        deadLetter.Error.Should().Be("PostgresException 23503", "a foreign key violation, without the message values");
    }

    [DockerFact]
    public async Task An_unreachable_database_keeps_the_messages_buffered_without_using_up_attempts()
    {
        // Nothing listens on this port: every save fails with a transient connection error.
        var (worker, buffer) = CreateWorker("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1");
        buffer.Enqueue(Message(Guid.NewGuid(), "hola"));

        for (var i = 0; i < ChatPersistenceWorker.MaxAttempts + 2; i++)
        {
            await worker.FlushAsync(CancellationToken.None);
        }

        buffer.PendingCount.Should().Be(1, "the message waits for the database instead of being dropped or dead-lettered");
    }
}
