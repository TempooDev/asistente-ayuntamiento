using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Prompts;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Users;

public class HistoryAnalyzerService(
    IAppDbContext dbContext,
    IUserPreferenceService preferenceService,
    Kernel kernel,
    IConfiguration configuration,
    ICurrentTenantService tenantService,
    ILlmUsageMetrics usageMetrics) : IHistoryAnalyzerService
{
    public async Task AnalyzeAndMergeUserHistoryAsync(string auth0UserId, CancellationToken cancellationToken = default)
    {
        // 1. Get the latest 5 sessions
        var recentSessions = await dbContext.ChatSessions
            .AsNoTracking()
            .Include(s => s.Messages)
            .Where(s => s.UserId == auth0UserId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (!recentSessions.Any()) return;

        var messagesContext = string.Join("\n\n", recentSessions.Select(s =>
            $"Sesión ({s.CreatedAt}):\n" +
            string.Join("\n", s.Messages.OrderBy(m => m.CreatedAt).Select(m => $"{m.Role}: {m.Content}"))
        ));

        if (messagesContext.Length > 8000)
        {
            messagesContext = messagesContext.Substring(0, 8000);
        }

        // 2. Extract using LLM
        var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
        var systemPrompt = PromptCatalog.HistoryAnalyzerSystem;

        var history = new ChatHistory(systemPrompt);
        history.AddUserMessage(messagesContext);

        var scope = new LlmCallScope(tenantService.TenantId ?? "unknown", LlmPipelines.Profile, LlmOperations.HistoryAnalysis);
        var result = await chatCompletion.CompleteMeteredAsync(history, PlatformModels.Chat(configuration), scope, usageMetrics, cancellationToken: cancellationToken);
        var resultContent = result.Content ?? string.Empty;

        resultContent = resultContent.Replace("```json", "").Replace("```", "").Trim();

        try
        {
            var extracted = JsonSerializer.Deserialize<UserPreferenceDto>(resultContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (extracted != null && (extracted.Topics.Any() || extracted.Locations.Any()))
            {
                // 3. Merge with existing preferences
                await preferenceService.MergePreferencesAsync(auth0UserId, extracted, cancellationToken);
            }
        }
        catch (JsonException)
        {
            // Ignore if the LLM failed to return valid JSON
        }
    }
}
