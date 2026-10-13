using System.Net;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>Stable error codes for LLM failures, safe to show to users (no provider details).</summary>
public static class LlmErrorCodes
{
    public const string QuotaExceeded = "LLM_QUOTA_EXCEEDED";
    public const string NotConfigured = "LLM_NOT_CONFIGURED";
    public const string Unavailable = "LLM_UNAVAILABLE";

    public static string UserMessage(string code) => code switch
    {
        QuotaExceeded => $"[{QuotaExceeded}] Hemos alcanzado el límite de uso del modelo de IA. Inténtalo de nuevo más tarde.",
        NotConfigured => $"[{NotConfigured}] El modelo de IA no está configurado. Contacta con el administrador.",
        _ => $"[{Unavailable}] El modelo de IA no está disponible en este momento. Inténtalo de nuevo más tarde.",
    };
}
