namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// The model stream failed, before any text or after part of the answer was sent. Carries the typed code
/// (<see cref="LlmErrorCodes"/>) so the caller can show that error (the chat as a message or after the partial text,
/// the Arena as a failed side); the message has no provider details.
/// </summary>
public sealed class LlmStreamInterruptedException(string errorCode, Exception innerException)
    : Exception(LlmErrorCodes.UserMessage(errorCode), innerException)
{
    public string ErrorCode { get; } = errorCode;
}
