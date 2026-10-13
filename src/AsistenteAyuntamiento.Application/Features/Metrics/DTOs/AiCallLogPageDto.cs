using AsistenteAyuntamiento.Domain.Features.Chat;

namespace AsistenteAyuntamiento.Application.Features.Metrics.DTOs;

public sealed record AiCallLogPageDto(int TotalItems, int Page, int PageSize, IReadOnlyList<AiCallLog> Items);
