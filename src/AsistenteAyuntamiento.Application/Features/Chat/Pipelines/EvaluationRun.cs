using System.Diagnostics;
using System.Text;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Chat.Pipelines;

/// <summary>
/// Measures one evaluation answer and builds its <see cref="RagEvaluationResult"/>, so every pipeline reports
/// timings, tokens and failures the same way: wrap each stage, then pass the answer stream to <see cref="AnswerAsync"/>.
/// </summary>
internal sealed class EvaluationRun(PipelineType pipeline) : IDisposable
{
    private static readonly string SourcesPrefix = "\n\n" + SourceCitations.Heading;

    private readonly LlmUsageCapture.Scope _usage = LlmUsageCapture.Begin();
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private double _expansionMs;
    private double _retrievalMs;

    public Task<T> ExpandAsync<T>(Func<Task<T>> expand) => TimeAsync(expand, ms => _expansionMs = ms);

    public Task<T> RetrieveAsync<T>(Func<Task<T>> retrieve) => TimeAsync(retrieve, ms => _retrievalMs = ms);

    /// <summary>
    /// Reads the answer and returns the result. Model errors become its error code with an empty answer. The
    /// "Fuentes consultadas" block the pipelines append is left out: it is built from the retrieval, not generated,
    /// and would inflate faithfulness and relevancy.
    /// </summary>
    public async Task<RagEvaluationResult> AnswerAsync(IAsyncEnumerable<string> stream, IReadOnlyList<RetrievedContext> contexts, string generationContext,
        CancellationToken cancellationToken)
    {
        var generation = Stopwatch.StartNew();
        double? timeToFirstToken = null;
        var answer = new StringBuilder();
        string? errorCode = null;
        try
        {
            await foreach (var chunk in stream.WithCancellation(cancellationToken))
            {
                if (string.IsNullOrEmpty(chunk) || chunk.StartsWith(SourcesPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                timeToFirstToken ??= generation.Elapsed.TotalMilliseconds;
                answer.Append(chunk);
            }
        }
        catch (LlmStreamInterruptedException ex)
        {
            errorCode = ex.ErrorCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errorCode = LlmErrorClassifier.Classify(ex);
        }

        return new RagEvaluationResult(pipeline, errorCode is null ? answer.ToString() : string.Empty, contexts, generationContext, _usage.Total,
            new RagEvaluationTimings(_expansionMs, _retrievalMs, timeToFirstToken, generation.Elapsed.TotalMilliseconds, _total.Elapsed.TotalMilliseconds),
            errorCode);
    }

    public void Dispose() => _usage.Dispose();

    private static async Task<T> TimeAsync<T>(Func<Task<T>> stage, Action<double> record)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await stage();
        }
        finally
        {
            record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
