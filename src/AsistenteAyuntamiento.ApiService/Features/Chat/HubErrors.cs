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

    public static string Message(string code) => code switch
    {
        Unauthorized => $"Error [{Unauthorized}]: no se ha podido identificar al usuario.",
        NotFound => $"Error [{NotFound}]: la conversación no existe o no tienes acceso.",
        InvalidRequest => $"Error [{InvalidRequest}]: la solicitud no es válida.",
        _ => $"Error [{Internal}]: se ha producido un error interno. Inténtalo de nuevo más tarde.",
    };
}
