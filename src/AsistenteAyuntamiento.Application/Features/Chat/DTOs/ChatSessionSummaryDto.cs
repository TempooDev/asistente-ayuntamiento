using System;

namespace AsistenteAyuntamiento.Application.Features.Chat.DTOs;

public record ChatSessionSummaryDto(Guid Id, DateTime CreatedAt, string Preview, int MessageCount);
