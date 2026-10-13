using AsistenteAyuntamiento.Application.Features.Chat;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

/// <summary>
/// Error messages sent to hub clients. They carry a stable code and never include exception details,
/// which are only written to the server logs.
/// </summary>
public static class HubErrors
{
    public const string Unauthorized = "UNAUTHORIZED";
    public const string NotFound = "NOT_FOUND";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string Internal = "INTERNAL";
    public const string AlreadyVoted = "ALREADY_VOTED";

    /// <summary>What the client shows for <see cref="Internal"/> (<c>stream-errors.ts</c>).</summary>
    private const string InternalNotice = "Se ha producido un error interno. Inténtalo de nuevo más tarde.";

    /// <summary>
    /// Markdown appended to a partial answer stored after its stream failed: the notice the client appends for the
    /// same error (<c>mapStreamError</c> in <c>stream-errors.ts</c>), for an <c>LLM_*</c> code or, when null, an
    /// internal error.
    /// </summary>
    public static string StreamFailureNotice(string? llmErrorCode) =>
        $"\n\n**⚠️ {(llmErrorCode is null ? InternalNotice : LlmErrorCodes.Description(llmErrorCode))}**";

    public static string Message(string code) => code switch
    {
        Unauthorized => $"Error [{Unauthorized}]: no se ha podido identificar al usuario.",
        NotFound => $"Error [{NotFound}]: la conversación no existe o no tienes acceso.",
        InvalidRequest => $"Error [{InvalidRequest}]: la solicitud no es válida.",
        AlreadyVoted => $"Error [{AlreadyVoted}]: esta comparación ya tiene un voto.",
        _ => $"Error [{Internal}]: se ha producido un error interno. Inténtalo de nuevo más tarde.",
    };
}
