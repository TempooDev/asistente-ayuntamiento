using System;

namespace AsistenteAyuntamiento.Application.Features.Chat.DTOs;

public record ChatMessageDto(string Role, string Content, DateTime CreatedAt);
