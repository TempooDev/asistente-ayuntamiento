using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>One user turn: the conversation so far (ending with the user's message) and who asked.</summary>
public sealed record ChatTurn(ChatHistory History, string TenantId, string UserId)
{
    /// <summary>Text of the latest user message, or empty.</summary>
    public string UserQuery { get; } = History.LastOrDefault(m => m.Role == AuthorRole.User)?.Content ?? string.Empty;

    /// <summary>Independent copy, for pipelines that rewrite the history (RAG context injection).</summary>
    public ChatTurn Copy() => this with { History = new ChatHistory(History.ToArray()) };
}
