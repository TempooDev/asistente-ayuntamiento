using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Application.Common.Interfaces;
namespace AsistenteAyuntamiento.Infrastructure.Features.Chat;

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using AsistenteAyuntamiento.Application.Features.Chat;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background worker that periodically flushes chat messages to the database.
/// </summary>
public class ChatPersistenceWorker(
    ChatMessageBuffer buffer,
    IServiceProvider serviceProvider,
    ILogger<ChatPersistenceWorker> logger) : BackgroundService
{
    private readonly ChatMessageBuffer _buffer = buffer;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<ChatPersistenceWorker> _logger = logger;
    // Only touched by the single flush loop (and the final flush after it ends), so no locking is needed.
    private readonly Dictionary<Guid, int> _retryCounts = new();

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

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var messages = _buffer.DrainAll();
        if (messages.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            // Create clean entity instances to guarantee EF Core doesn't pull in 
            // the parent ChatSession via any lingering navigation properties or tracking
            var entitiesToInsert = messages.Select(m => new ChatMessage
            {
                Id = m.Id,
                SessionId = m.SessionId,
                Role = m.Role,
                Content = m.Content,
                CreatedAt = m.CreatedAt
            }).ToList();

            dbContext.ChatMessages.AddRange(entitiesToInsert);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Flushed {Count} messages to database", messages.Count);

            foreach (var msg in messages)
            {
                _retryCounts.Remove(msg.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush {Count} messages. Checking retry limits: {Error}", messages.Count, ex.Message);

            foreach (var msg in messages)
            {
                var retries = _retryCounts.GetValueOrDefault(msg.Id, 0) + 1;
                if (retries <= 3)
                {
                    _retryCounts[msg.Id] = retries;
                    _buffer.Enqueue(msg);
                }
                else
                {
                    // Content is not logged (it may contain personal data); ids allow tracing the lost message.
                    _logger.LogCritical(
                        "Chat message {Id} of session {SessionId} ({Role}, {CreatedAt:O}, {Length} chars) could not be persisted after 3 retries and was dropped",
                        msg.Id, msg.SessionId, msg.Role, msg.CreatedAt, msg.Content?.Length ?? 0);
                    _retryCounts.Remove(msg.Id);
                }
            }
        }
    }
}
