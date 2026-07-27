using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AsistenteAyuntamiento.ApiService.Features.Tenants;
using AsistenteAyuntamiento.ApiService.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

[Authorize]
public class ChatHub : Hub
{
    private readonly CurrentTenantService _tenantService;
    private readonly ILogger<ChatHub> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ChatHub(CurrentTenantService tenantService, ILogger<ChatHub> logger, IServiceProvider serviceProvider)
    {
        _tenantService = tenantService;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task SendMessage(string message)
    {
        try
        {
            var auth0Id = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = _tenantService.TenantId;

            if (string.IsNullOrEmpty(auth0Id))
            {
                await Clients.Caller.SendAsync("ReceiveMessage", "Error: No user ID found.");
                return;
            }

            _logger.LogInformation("Received message from {User} in Tenant {Tenant}: {Message}", auth0Id, tenantId, message);

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var chatCompletionService = scope.ServiceProvider.GetService<IChatCompletionService>();

            if (chatCompletionService == null)
            {
                await Clients.Caller.SendAsync("ReceiveMessage", "Error: Chat AI service is not available.");
                return;
            }

            // 1. Lógica del Historial y Retención (< 1 semana)
            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            
            // Find most recent session for this user/tenant, or create new
            var session = await dbContext.ChatSessions
                .Include(s => s.Messages.Where(m => m.CreatedAt >= sevenDaysAgo))
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(s => s.UserId == auth0Id && s.TenantId == tenantId);

            if (session == null || session.CreatedAt < sevenDaysAgo)
            {
                session = new ChatSession
                {
                    Id = Guid.NewGuid(),
                    UserId = auth0Id,
                    TenantId = tenantId,
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.ChatSessions.Add(session);
            }

            // Add user message
            var userMsg = new ChatMessage
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                Role = "user",
                Content = message,
                CreatedAt = DateTime.UtcNow
            };
            session.Messages.Add(userMsg);
            dbContext.ChatMessages.Add(userMsg);

            await dbContext.SaveChangesAsync();

            // 2. Mecanismo de compactación
            // Keep only the last 20 messages to avoid token overflow
            var recentMessages = session.Messages.OrderBy(m => m.CreatedAt).TakeLast(20).ToList();

            // Build Semantic Kernel ChatHistory
            var history = new ChatHistory("Eres un asistente virtual del ayuntamiento. Responde de forma clara y concisa en español.");
            foreach (var msg in recentMessages)
            {
                if (msg.Role == "user")
                    history.AddUserMessage(msg.Content);
                else if (msg.Role == "assistant")
                    history.AddAssistantMessage(msg.Content);
            }

            // 3. Obtener respuesta de Ollama
            string responseContent = "Lo siento, ha ocurrido un error al procesar tu solicitud.";
            try
            {
                var response = await chatCompletionService.GetChatMessageContentAsync(history);
                responseContent = response.Content ?? "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling IChatCompletionService");
                responseContent = "Error de comunicación con el modelo de IA local: " + ex.Message;
            }

            // Guardar respuesta del asistente
            var assistantMsg = new ChatMessage
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                Role = "assistant",
                Content = responseContent,
                CreatedAt = DateTime.UtcNow
            };
            session.Messages.Add(assistantMsg);
            dbContext.ChatMessages.Add(assistantMsg);

            await dbContext.SaveChangesAsync();

            // 4. Enviar al cliente
            await Clients.Caller.SendAsync("ReceiveMessage", responseContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Global exception in SendMessage");
            await Clients.Caller.SendAsync("ReceiveMessage", $"System Error: {ex.GetType().Name} - {ex.Message}");
        }
    }
}
