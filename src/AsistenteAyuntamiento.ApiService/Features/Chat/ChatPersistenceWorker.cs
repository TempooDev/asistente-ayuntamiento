namespace AsistenteAyuntamiento.ApiService.Features.Chat;

using System;
using System.Threading;
using System.Threading.Tasks;
using AsistenteAyuntamiento.ApiService.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background worker that periodically flushes chat messages to the database.
/// </summary>
public class ChatPersistenceWorker : BackgroundService
{
    private readonly ChatMessageBuffer _buffer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChatPersistenceWorker> _logger;

    public ChatPersistenceWorker(
        ChatMessageBuffer buffer,
        IServiceProvider serviceProvider,
        ILogger<ChatPersistenceWorker> logger)
    {
        _buffer = buffer;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

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
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush {Count} messages, re-queued for retry: {Error}", messages.Count, ex.Message);
            
            foreach (var msg in messages)
            {
                _buffer.Enqueue(msg);
            }
        }
    }
}
