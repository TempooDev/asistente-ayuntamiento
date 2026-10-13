using AsistenteAyuntamiento.Application.Features.Chat;

namespace AsistenteAyuntamiento.Application.Features.Arena;

/// <summary>
/// A side of an Arena page comparison failed (<see cref="ErrorCode"/>, see <see cref="LlmErrorCodes"/>), so the battle
/// was not saved and cannot be voted on. The message is the user-facing text, without provider details.
/// </summary>
public sealed class ArenaComparisonFailedException(string errorCode)
    : InvalidOperationException(LlmErrorCodes.Description(errorCode))
{
    public string ErrorCode { get; } = errorCode;
}
