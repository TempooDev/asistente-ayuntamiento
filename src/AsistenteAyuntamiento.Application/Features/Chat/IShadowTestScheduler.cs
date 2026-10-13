using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// Shadow testing: for a sample of answers, runs the losing pipeline in the background and stores both answers as
/// an Arena battle, so users can compare them later.
/// </summary>
public interface IShadowTestScheduler
{
    /// <summary>Whether this answer is sampled for a shadow test.</summary>
    bool ShouldRun();

    /// <summary>
    /// Queues the run of the pipeline that did not answer. <paramref name="turn"/> must be an untouched copy of
    /// the user's turn (see <see cref="ChatTurn.Copy"/>).
    /// </summary>
    void Schedule(ChatTurn turn, PipelineType answeredBy, string answer, long answerLatencyMs);
}
