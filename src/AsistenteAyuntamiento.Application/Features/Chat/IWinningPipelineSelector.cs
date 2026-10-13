using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>Chooses the pipeline that answers users, from the Arena votes.</summary>
public interface IWinningPipelineSelector
{
    Task<PipelineType> GetWinnerAsync(CancellationToken cancellationToken = default);
}
