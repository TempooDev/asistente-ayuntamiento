using System.ClientModel;
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

/// <summary>The tenant's provider requires an API key and none is configured.</summary>
public sealed class LlmNotConfiguredException(string message) : InvalidOperationException(message);

public static class LlmErrorClassifier
{
    /// <summary>
    /// True only for HTTP 429 from the provider (anywhere in the exception chain).
    /// Message text is deliberately not inspected: words like "limit" appear in unrelated errors.
    /// </summary>
    public static bool IsQuotaExceeded(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            var isTooManyRequests = ex switch
            {
                HttpOperationException http => http.StatusCode == HttpStatusCode.TooManyRequests,
                HttpRequestException http => http.StatusCode == HttpStatusCode.TooManyRequests,
                ClientResultException client => client.Status == (int)HttpStatusCode.TooManyRequests,
                _ => false,
            };

            if (isTooManyRequests)
            {
                return true;
            }
        }

        return false;
    }

    public static string Classify(Exception exception) => exception switch
    {
        _ when IsQuotaExceeded(exception) => LlmErrorCodes.QuotaExceeded,
        LlmNotConfiguredException => LlmErrorCodes.NotConfigured,
        _ => LlmErrorCodes.Unavailable,
    };
}
