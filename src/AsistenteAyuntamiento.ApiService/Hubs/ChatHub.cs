using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Runtime.CompilerServices;

namespace AsistenteAyuntamiento.ApiService.Hubs;

[Authorize(Roles = "Administrador,Funcionario")]
public class ChatHub : Hub
{
    private readonly IChatCompletionService? _chatCompletionService;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(ILogger<ChatHub> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        // Optional for now, until SK is fully registered in Program.cs
        _chatCompletionService = serviceProvider.GetService<IChatCompletionService>();
    }

    public async IAsyncEnumerable<string> StreamChat(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = Context.UserIdentifier;
        _logger.LogInformation("User {UserId} started chat stream: {Message}", userId, message);

        // TODO: Perform RAG similarity search and get citations
        // var sources = await _ragService.SearchAsync(message);
        var mockSources = new[] 
        { 
            new { Title = "BOE-A-2023-123", Url = "https://boe.es/..." },
            new { Title = "BOJA-2023-456", Url = "https://juntadeandalucia.es/..." }
        };

        // 1. Deliver citations out-of-band before streaming text
        await Clients.Caller.SendAsync("ReceiveSources", mockSources, cancellationToken);

        // 2. Stream the LLM response
        if (_chatCompletionService != null)
        {
            var history = new ChatHistory();
            history.AddUserMessage(message);

            await foreach (var content in _chatCompletionService.GetStreamingChatMessageContentsAsync(history, cancellationToken: cancellationToken))
            {
                if (content.Content != null)
                {
                    yield return content.Content;
                }
            }
        }
        else
        {
            // Fallback mock stream if SK is not yet registered
            var mockResponse = $"Echo from Hub (User: {userId}): {message} ".Split(' ');
            foreach (var word in mockResponse)
            {
                await Task.Delay(100, cancellationToken); // Simulate streaming latency
                yield return word + " ";
            }
        }
    }
}
