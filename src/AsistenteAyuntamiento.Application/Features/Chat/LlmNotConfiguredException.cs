using System.Net;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>The tenant's provider requires an API key and none is configured.</summary>
public sealed class LlmNotConfiguredException(string message) : InvalidOperationException(message);
