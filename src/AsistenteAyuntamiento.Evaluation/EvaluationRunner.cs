using System.Text.Json;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>Where a run writes and what it asks.</summary>
/// <param name="RunDirectory"><c>eval/runs/{runId}</c>; an existing <c>answers.jsonl</c> there is resumed.</param>
public sealed record EvaluationRunSettings(string RunDirectory, IReadOnlyList<PipelineType> Pipelines, int Seed, RunInfo Info);

/// <summary>
/// Asks every question to every selected pipeline in-process (spec: rag-evaluation, Evaluation Runner) and appends
/// each answer to <c>answers.jsonl</c> as soon as it is ready, so an interrupted run loses at most one answer.
/// </summary>
public sealed class EvaluationRunner(IServiceScopeFactory scopeFactory, ILogger<EvaluationRunner> logger)
{
    public const string AnswersFile = "answers.jsonl";
    public const string RunFile = "run.json";
    public const string IndexingFile = "indexing.json";

    public async Task RunAsync(EvaluationDataset dataset, EvaluationRunSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(settings.RunDirectory);
        var answersPath = Path.Combine(settings.RunDirectory, AnswersFile);
        var done = ReadCompleted(answersPath);
        if (done.Count > 0)
        {
            logger.LogInformation("Resuming: {Count} answers already in {Path}", done.Count, answersPath);
        }

        await WriteJsonAsync(Path.Combine(settings.RunDirectory, RunFile), settings.Info, cancellationToken);

        // The order of the pipelines changes per question (seeded) so no variant always runs on a warm or cold model.
        var random = new Random(settings.Seed);
        await using var answers = new StreamWriter(answersPath, append: true);
        for (var position = 0; position < dataset.Questions.Count; position++)
        {
            var question = dataset.Questions[position];
            var order = settings.Pipelines.ToArray();
            random.Shuffle(order);

            foreach (var pipeline in order)
            {
                if (done.Contains((question.Id, pipeline)))
                {
                    continue;
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetServices<IRagPipeline>().Get(pipeline)
                    .RunForEvaluationAsync(new EvaluationTurn(question.Question), cancellationToken);

                await answers.WriteLineAsync(JsonSerializer.Serialize(AnswerLine.From(question, position, result), EvaluationJson.Options));
                await answers.FlushAsync(cancellationToken);
                logger.LogInformation("{Question} {Pipeline}: {TotalMs:F0} ms, {Contexts} contexts{Error}",
                    question.Id, pipeline, result.Timings.TotalMs, result.Contexts.Count, result.ErrorCode is null ? "" : $", error {result.ErrorCode}");
            }
        }

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var indexing = await scope.ServiceProvider.GetRequiredService<IndexingReporter>().BuildAsync(settings.Pipelines, dataset.CorpusDocumentIds, cancellationToken);
            await WriteJsonAsync(Path.Combine(settings.RunDirectory, IndexingFile), indexing, cancellationToken);
        }

        await WriteJsonAsync(Path.Combine(settings.RunDirectory, RunFile), settings.Info with { FinishedAt = DateTime.UtcNow }, cancellationToken);
    }

    /// <summary>The question and pipeline pairs already answered in a previous attempt of the run.</summary>
    internal static HashSet<(string QuestionId, PipelineType Pipeline)> ReadCompleted(string answersPath)
    {
        if (!File.Exists(answersPath))
        {
            return [];
        }

        return File.ReadLines(answersPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<AnswerKey>(line, EvaluationJson.Options)!)
            .Select(key => (key.QuestionId, key.Pipeline))
            .ToHashSet();
    }

    internal static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, EvaluationJson.Indented, cancellationToken);
    }

    private sealed record AnswerKey(string QuestionId, PipelineType Pipeline);
}
