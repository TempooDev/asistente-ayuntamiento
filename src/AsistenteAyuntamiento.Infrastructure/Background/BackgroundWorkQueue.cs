using System.Threading.Channels;
using AsistenteAyuntamiento.Application.Common.Background;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Infrastructure.Background;

/// <summary>Bounded in-memory queue of <see cref="BackgroundWorkItem"/>s, drained by <see cref="BackgroundWorkProcessor"/>.</summary>
public sealed class BackgroundWorkQueue : IBackgroundWorkQueue
{
    public const int Capacity = 200;

    private readonly Channel<BackgroundWorkItem> _channel = Channel.CreateBounded<BackgroundWorkItem>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public bool TryEnqueue(BackgroundWorkItem item) => _channel.Writer.TryWrite(item);

    internal ChannelReader<BackgroundWorkItem> Reader => _channel.Reader;
}

/// <summary>
/// Runs queued work items concurrently (bounded), each in its own DI scope and under its tenant.
/// Failures are logged, never lost; in-flight items receive the shutdown token and are awaited on stop.
/// </summary>
public sealed class BackgroundWorkProcessor(
    BackgroundWorkQueue queue,
    IServiceScopeFactory scopeFactory,
    ICurrentTenantService tenantService,
    ILogger<BackgroundWorkProcessor> logger) : BackgroundService
{
    public const int MaxConcurrency = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var slots = new SemaphoreSlim(MaxConcurrency);
        var running = new List<Task>();

        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await slots.WaitAsync(stoppingToken);
                running.RemoveAll(t => t.IsCompleted);
                running.Add(RunAsync(item, slots, stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is stopping.
        }

        await Task.WhenAll(running);
    }

    private async Task RunAsync(BackgroundWorkItem item, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        try
        {
            // AsyncLocal: the tenant applies only to this item's async flow.
            if (item.TenantId is not null)
            {
                tenantService.SetTenant(item.TenantId);
            }

            using var scope = scopeFactory.CreateScope();
            await item.Work(scope.ServiceProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Background work {Name} was cancelled by host shutdown", item.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background work {Name} failed (tenant {TenantId})", item.Name, item.TenantId ?? "platform");
        }
        finally
        {
            slots.Release();
        }
    }
}
