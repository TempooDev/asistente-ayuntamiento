using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Chat;

public sealed record FallbackChatModel(string ModelId, IChatCompletionService Service);
