using AsistenteAyuntamiento.Application.Features.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Infrastructure.Data;

public sealed class IngestionMaintenance(AppDbContext dbContext) : IIngestionMaintenance
{
    public async Task ClearBaselineDataAsync(CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational())
        {
            await dbContext.DocumentChunks.ExecuteDeleteAsync(cancellationToken);
            await dbContext.DocumentJobStates.ExecuteDeleteAsync(cancellationToken);
            return;
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE ingestion.\"DocumentChunks\"; TRUNCATE TABLE public.\"DocumentJobStates\";", cancellationToken);
    }
}
