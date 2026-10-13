namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// The model stream failed after part of the answer was already sent. Carries the typed code
/// (<see cref="LlmErrorCodes"/>) so the caller can end the stream with that error; the message has no provider details.
/// </summary>
public sealed class LlmStreamInterruptedException(string errorCode, Exception innerException)
    : Exception(LlmErrorCodes.UserMessage(errorCode), innerException)
{
    public string ErrorCode { get; } = errorCode;
}
