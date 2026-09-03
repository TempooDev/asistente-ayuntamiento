using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Users;
using AsistenteAyuntamiento.Application.Common.Interfaces;
namespace AsistenteAyuntamiento.ApiService.Features.Notifications;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

[Authorize]
public class NotificationHub : Hub
{
    // Hub specifically for system-wide events and notifications
}
