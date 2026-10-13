using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AsistenteAyuntamiento.Infrastructure.Features.Chat;

/// <summary>
/// Flushes the buffered chat messages to the database every 2 seconds.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A failed batch is retried message by message, so one bad message (for example, of a session that was
/// deleted meanwhile) does not take the rest of the batch with it.</item>
/// <item>Transient failures (database down, timeouts) keep the messages in the buffer without using up attempts,
/// up to <see cref="MaxBufferedMessages"/>.</item>
/// <item>Other failures are retried <see cref="MaxAttempts"/> times; then the message goes to
/// <c>chat.FailedChatMessages</c> (dead letter). Only if that also fails is it dropped, with a critical log that
/// never includes the content.</item>
/// </list>
/// </remarks>
public class ChatPersistenceWorker(
    ChatMessageBuffer buffer,
    IServiceProvider serviceProvider,
    ILogger<ChatPersistenceWorker> logger) : BackgroundService
{
    internal const int MaxAttempts = 3;

    /// <summary>Messages kept in memory while the database is unreachable; beyond this they are dead-lettered.</summary>
    internal const int MaxBufferedMessages = 10_000;

    // Only touched by the single flush loop (and the final flush after it ends), so no locking is needed.
    private readonly Dictionary<Guid, int> _attempts = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Application is shutting down gracefully
        }

        // Final flush to ensure no messages are lost on shutdown
        await FlushAsync(CancellationToken.None);
    }

    internal async Task FlushAsync(CancellationToken cancellationToken)
    {
        var messages = buffer.DrainAll();
        if (messages.Count == 0)
        {
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        try
        {
            dbContext.ChatMessages.AddRange(messages.Select(Copy));
            await dbContext.SaveChangesAsync(cancellationToken);
            Forget(messages);
            logger.LogInformation("Flushed {Count} messages to database", messages.Count);
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: the final flush (without cancellation) saves them.
            foreach (var message in messages) buffer.Enqueue(message);
            throw;
        }
        catch (Exception ex)
        {
            dbContext.ChangeTracker.Clear();
            if (IsTransient(ex))
            {
                KeepForLater(messages, ex);
                return;
            }
            logger.LogWarning(ex, "Saving {Count} chat messages failed; saving them one by one", messages.Count);
        }

        foreach (var message in messages)
        {
            await SaveOneAsync(dbContext, message, cancellationToken);
        }
    }

    private async Task SaveOneAsync(IAppDbContext dbContext, ChatMessage message, CancellationToken cancellationToken)
    {
        try
        {
            dbContext.ChatMessages.Add(Copy(message));
            await dbContext.SaveChangesAsync(cancellationToken);
            Forget([message]);
        }
        catch (Exception ex)
        {
            dbContext.ChangeTracker.Clear();
            if (IsTransient(ex))
            {
                KeepForLater([message], ex);
                return;
            }

            var attempts = _attempts.GetValueOrDefault(message.Id) + 1;
            if (attempts < MaxAttempts)
            {
                _attempts[message.Id] = attempts;
                buffer.Enqueue(message);
                return;
            }

            await DeadLetterAsync(dbContext, message, attempts, ex, cancellationToken);
        }
    }

    private void KeepForLater(IReadOnlyList<ChatMessage> messages, Exception ex)
    {
        if (buffer.PendingCount + messages.Count > MaxBufferedMessages)
        {
            // The database has been unreachable for long: stop growing the buffer.
            foreach (var message in messages)
            {
                LogDropped(message, MaxAttempts, ex);
                _attempts.Remove(message.Id);
            }
            return;
        }

        logger.LogWarning(ex, "Database unavailable; keeping {Count} chat messages buffered", messages.Count);
        foreach (var message in messages)
        {
            buffer.Enqueue(message);
        }
    }

    private async Task DeadLetterAsync(IAppDbContext dbContext, ChatMessage message, int attempts, Exception error, CancellationToken cancellationToken)
    {
        _attempts.Remove(message.Id);
        try
        {
            dbContext.FailedChatMessages.Add(new FailedChatMessage
            {
                Id = message.Id,
                SessionId = message.SessionId,
                Role = message.Role,
                Content = message.Content,
                CreatedAt = message.CreatedAt,
                Attempts = attempts,
                Error = Describe(error),
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogError(
                "Chat message {Id} of session {SessionId} could not be saved after {Attempts} attempts ({Error}); moved to chat.FailedChatMessages",
                message.Id, message.SessionId, attempts, Describe(error));
        }
        catch (Exception deadLetterError)
        {
            dbContext.ChangeTracker.Clear();
            LogDropped(message, attempts, deadLetterError);
        }
    }

    private void LogDropped(ChatMessage message, int attempts, Exception error) =>
        // Content is not logged (it may contain personal data); ids allow tracing the lost message.
        logger.LogCritical(error,
            "Chat message {Id} of session {SessionId} ({Role}, {CreatedAt:O}, {Length} chars) was dropped after {Attempts} attempts",
            message.Id, message.SessionId, message.Role, message.CreatedAt, message.Content?.Length ?? 0, attempts);

    private void Forget(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            _attempts.Remove(message.Id);
        }
    }

    /// <summary>A clean entity, so EF Core does not pull in the session through a navigation property.</summary>
    private static ChatMessage Copy(ChatMessage m) => new()
    {
        Id = m.Id,
        SessionId = m.SessionId,
        Role = m.Role,
        Content = m.Content,
        CreatedAt = m.CreatedAt,
    };

    /// <summary>The database could not be reached; retrying later can succeed without changing the message.</summary>
    /// <remarks>EF Core wraps connection errors (DbUpdateException, or InvalidOperationException when no retrying
    /// strategy is configured), so the whole chain of inner exceptions is checked.</remarks>
    internal static bool IsTransient(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException or RetryLimitExceededException) return true;
            if (current is NpgsqlException npgsql) return npgsql.IsTransient;
        }
        return false;
    }

    private static string Describe(Exception ex)
    {
        var root = ex is DbUpdateException { InnerException: { } inner } ? inner : ex;
        var description = root is PostgresException postgres ? $"{root.GetType().Name} {postgres.SqlState}" : root.GetType().Name;
        return description.Length <= 300 ? description : description[..300];
    }
}
