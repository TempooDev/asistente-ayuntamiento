namespace AsistenteAyuntamiento.Application.Features.Chat.DTOs;

using System;

public record ChatMessageDto(string Role, string Content, DateTime CreatedAt);
