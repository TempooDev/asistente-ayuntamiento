using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// The conversation before the current question, bounded so it fits next to the retrieved documents. Follow-up
/// questions ("el convenio", "estas ayudas") only make sense with it: the search needs it to find the right document
/// and the model to answer about it.
/// </summary>
public static class ConversationContext
{
    /// <summary>Previous messages kept (three question-answer pairs).</summary>
    public const int MaxMessages = 6;

    /// <summary>Characters kept per message; answers are long and the documents need the room.</summary>
    public const int MaxCharsPerMessage = 1_500;

    /// <summary>
    /// User and assistant messages before the latest user message, the most recent <see cref="MaxMessages"/>, each cut
    /// at <see cref="MaxCharsPerMessage"/>. Empty for the first question of a conversation.
    /// </summary>
    public static IReadOnlyList<ChatMessageContent> Previous(ChatHistory history)
    {
        var lastUser = -1;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Role == AuthorRole.User)
            {
                lastUser = i;
                break;
            }
        }

        return history
            .Take(Math.Max(lastUser, 0))
            .Where(m => (m.Role == AuthorRole.User || m.Role == AuthorRole.Assistant) && !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(MaxMessages)
            .Select(m => new ChatMessageContent(m.Role, Cut(m.Content!)))
            .ToList();
    }

    /// <summary>The previous messages as plain text for a prompt ("Usuario: …" / "Asistente: …"), or "(ninguna)".</summary>
    public static string Describe(IReadOnlyList<ChatMessageContent> previous)
    {
        if (previous.Count == 0)
        {
            return "(ninguna)";
        }

        // '\n', not AppendLine: the prompt is the same on every OS.
        return string.Join('\n', previous.Select(m => (m.Role == AuthorRole.User ? "Usuario: " : "Asistente: ") + m.Content));
    }

    private static string Cut(string content) =>
        content.Length <= MaxCharsPerMessage ? content : content[..MaxCharsPerMessage] + " […]";
}
