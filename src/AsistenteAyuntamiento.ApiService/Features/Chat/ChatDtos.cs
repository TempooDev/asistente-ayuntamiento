namespace AsistenteAyuntamiento.ApiService.Features.Chat;

using System;

public record ChatSessionSummaryDto(Guid Id, DateTime CreatedAt, string Preview, int MessageCount);
public record ChatMessageDto(string Role, string Content, DateTime CreatedAt);
