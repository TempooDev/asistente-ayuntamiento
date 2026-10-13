using System.Net;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>Stable error codes for LLM failures, safe to show to users (no provider details).</summary>
public static class LlmErrorCodes
{
    public const string QuotaExceeded = "LLM_QUOTA_EXCEEDED";
    public const string NotConfigured = "LLM_NOT_CONFIGURED";
    public const string Unavailable = "LLM_UNAVAILABLE";

    /// <summary>The message with its <c>[code]</c> prefix; an unknown code is reported as <see cref="Unavailable"/>.</summary>
    public static string UserMessage(string code)
    {
        var known = code is QuotaExceeded or NotConfigured ? code : Unavailable;
        return $"[{known}] {Description(known)}";
    }

    /// <summary>The message without its code: the same text the client shows for it (<c>stream-errors.ts</c>).</summary>
    public static string Description(string code) => code switch
    {
        QuotaExceeded => "Hemos alcanzado el límite de uso del modelo de IA. Inténtalo de nuevo más tarde.",
        NotConfigured => "El modelo de IA no está configurado. Contacta con el administrador.",
        _ => "El modelo de IA no está disponible en este momento. Inténtalo de nuevo más tarde.",
    };
}
