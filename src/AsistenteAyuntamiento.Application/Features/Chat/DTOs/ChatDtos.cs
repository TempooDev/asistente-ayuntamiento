using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Users;
using AsistenteAyuntamiento.Application.Common.Interfaces;
namespace AsistenteAyuntamiento.Application.Features.Chat.DTOs;

using System;

public record ChatSessionSummaryDto(Guid Id, DateTime CreatedAt, string Preview, int MessageCount);
public record ChatMessageDto(string Role, string Content, DateTime CreatedAt);
