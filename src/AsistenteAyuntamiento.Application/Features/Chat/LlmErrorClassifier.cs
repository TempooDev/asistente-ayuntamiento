using System.ClientModel;
using System.Net;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Chat;

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
