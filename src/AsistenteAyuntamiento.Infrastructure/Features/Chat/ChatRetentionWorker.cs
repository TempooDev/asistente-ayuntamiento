using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Infrastructure.Features.Chat;

/// <summary>
/// Deletes, once a day, the chat sessions (and their messages) with no activity for <c>Chat:RetentionDays</c> days,
/// across all tenants. 0 or unset keeps the history forever, as before. Lets a deployment state a retention period
/// for personal data (GDPR storage limitation) without manual clean-ups.
/// </summary>
public class ChatRetentionWorker(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ChatRetentionWorker> logger) : BackgroundService
{
    public const string RetentionDaysKey = "Chat:RetentionDays";

    /// <summary>Sessions deleted per round trip, so a first run over a large history does not load it whole.</summary>
    internal const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retentionDays = configuration.GetValue(RetentionDaysKey, 0);
        if (retentionDays <= 0)
        {
            logger.LogInformation("Chat retention disabled ({Key} = {Days}): the history is kept", RetentionDaysKey, retentionDays);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), timeProvider);
        try
        {
            do
            {
                try
                {
                    await PurgeAsync(retentionDays, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Chat retention run failed; retrying on the next run");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Application is shutting down
        }
    }

    /// <summary>Deletes the sessions whose last activity is older than <paramref name="retentionDays"/> days.</summary>
    /// <returns>The number of sessions deleted.</returns>
    internal async Task<int> PurgeAsync(int retentionDays, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var deleted = 0;

        while (true)
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            // Platform-wide job: no tenant in scope, so the tenant filters are bypassed on purpose.
            var expired = await db.ChatSessions
                .IgnoreQueryFilters()
                .Where(s => s.CreatedAt < cutoff && !s.Messages.Any(m => m.CreatedAt >= cutoff))
                .OrderBy(s => s.CreatedAt)
                .Take(BatchSize)
                .Include(s => s.Messages)
                .ToListAsync(cancellationToken);

            if (expired.Count == 0)
            {
                break;
            }

            db.ChatMessages.RemoveRange(expired.SelectMany(s => s.Messages));
            db.ChatSessions.RemoveRange(expired);
            await db.SaveChangesAsync(cancellationToken);
            deleted += expired.Count;

            if (expired.Count < BatchSize)
            {
                break;
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation("Chat retention deleted {Count} sessions inactive since {Cutoff:O}", deleted, cutoff);
        }

        return deleted;
    }
}
