namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>Single place that persists <c>AiCallLog</c> rows and emits AI metrics.</summary>
public interface IAiCallRecorder
{
    /// <summary>Records the call. Never throws: recording problems are logged and must not fail the user's request.</summary>
    Task RecordAsync(AiCallOutcome outcome, CancellationToken cancellationToken = default);
}
