using System.Globalization;
using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Features.Evaluation;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>
/// Stores a scored run (spec: evaluation-results, Importing a Scored Run): reads what the runner and the scorer wrote in
/// <c>eval/runs/{runId}</c> and replaces the run's rows, so a run scored again is imported again.
/// </summary>
public sealed class EvaluationImporter(IAppDbContext dbContext)
{
    public const string SummaryFile = "summary.csv";
    public const string ScoringFile = "scoring.json";
    public const string LatencyFile = "latency.csv";
    public const string IndexingCsvFile = "indexing.csv";

    /// <exception cref="FileNotFoundException">The run has not been scored (no summary or scoring file).</exception>
    public async Task<EvaluationRun> ImportAsync(string runDirectory, CancellationToken cancellationToken = default)
    {
        foreach (var required in new[] { EvaluationRunner.RunFile, EvaluationRunner.AnswersFile, SummaryFile, ScoringFile })
        {
            var path = Path.Combine(runDirectory, required);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"{required} not found in {runDirectory}; run and score the run first (score.py, report.py).", path);
            }
        }

        var info = JsonSerializer.Deserialize<RunInfo>(await File.ReadAllTextAsync(Path.Combine(runDirectory, EvaluationRunner.RunFile), cancellationToken), EvaluationJson.Options)!;
        var scoring = JsonSerializer.Deserialize<ScoringInfo>(await File.ReadAllTextAsync(Path.Combine(runDirectory, ScoringFile), cancellationToken), EvaluationJson.Options)!;
        var summary = CsvTable.Read(Path.Combine(runDirectory, SummaryFile));
        var latency = CsvTable.ReadIfExists(Path.Combine(runDirectory, LatencyFile));
        var indexing = CsvTable.ReadIfExists(Path.Combine(runDirectory, IndexingCsvFile));
        var tokens = MeanTokens(Path.Combine(runDirectory, EvaluationRunner.AnswersFile));

        var run = new EvaluationRun
        {
            RunId = info.RunId,
            StartedAt = info.StartedAt,
            FinishedAt = info.FinishedAt,
            GitCommit = info.GitCommit,
            Seed = info.Seed,
            ChatModel = info.ChatModel,
            EmbeddingModel = info.EmbeddingModel,
            JudgeModel = scoring.JudgeModel ?? info.ChatModel,
            QueryInstruction = info.QueryInstruction,
            QuestionCount = info.QuestionCount,
            DatasetHash = info.DatasetHash,
            CorpusHash = info.CorpusHash,
            RagasVersion = scoring.RagasVersion,
            ImportedAt = DateTime.UtcNow,
            Results = [.. summary.Rows.Select(row =>
            {
                var pipeline = row.Text("pipeline");
                var stage = (string name) => latency.Rows.FirstOrDefault(l => l.Text("pipeline") == pipeline && l.Text("stage") == name);
                var cost = indexing.Rows.FirstOrDefault(i => i.Text("pipeline") == pipeline);
                return new EvaluationPipelineResult
                {
                    RunId = info.RunId,
                    Pipeline = pipeline,
                    ContextPrecision = row.Number("context_precision_mean"),
                    ContextPrecisionStd = row.Number("context_precision_std"),
                    ContextRecall = row.Number("context_recall_mean"),
                    ContextRecallStd = row.Number("context_recall_std"),
                    Faithfulness = row.Number("faithfulness_mean"),
                    FaithfulnessStd = row.Number("faithfulness_std"),
                    AnswerRelevancy = row.Number("answer_relevancy_mean"),
                    AnswerRelevancyStd = row.Number("answer_relevancy_std"),
                    HallucinationRate = row.Number("hallucination_rate"),
                    HitAt1 = row.Number("hit@1_mean"),
                    HitAt3 = row.Number("hit@3_mean"),
                    HitAt5 = row.Number("hit@5_mean"),
                    Mrr = row.Number("reciprocal_rank_mean"),
                    NoAnswerDetectedRate = row.Number("no_answer_detected_rate"),
                    Failed = (int)(row.Number("failed") ?? 0),
                    Missing = (int)(row.Number("missing") ?? 0),
                    LatencyP50Ms = stage("total")?.Number("p50_ms"),
                    LatencyP95Ms = stage("total")?.Number("p95_ms"),
                    TtftP50Ms = stage("ttft")?.Number("p50_ms"),
                    TtftP95Ms = stage("ttft")?.Number("p95_ms"),
                    MeanTokens = tokens.GetValueOrDefault(pipeline),
                    IndexingP50Ms = cost?.Number("p50_ms_per_document"),
                    IndexingP95Ms = cost?.Number("p95_ms_per_document"),
                    ChunksPerDocument = cost?.Number("mean_chunks_per_document"),
                    Points = cost?.Number("points") is { } points ? (long)points : null,
                };
            })],
        };

        var existing = await dbContext.EvaluationRuns.Include(r => r.Results).FirstOrDefaultAsync(r => r.RunId == run.RunId, cancellationToken);
        if (existing is not null)
        {
            dbContext.EvaluationRuns.Remove(existing);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        dbContext.EvaluationRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return run;
    }

    /// <summary>Mean input plus output tokens of the successful answers of each pipeline.</summary>
    private static Dictionary<string, double> MeanTokens(string answersPath) => File.ReadLines(answersPath)
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(line => JsonSerializer.Deserialize<AnswerLine>(line, EvaluationJson.Options)!)
        .Where(a => a.ErrorCode is null)
        .GroupBy(a => a.Pipeline.ToString())
        .ToDictionary(g => g.Key, g => Math.Round(g.Average(a => (double)a.InputTokens + a.OutputTokens), 1));

    private sealed record ScoringInfo(string? JudgeModel, string? RagasVersion);
}

/// <summary>The CSV files of the Python scorer: a header row, comma-separated values without quoting, <c>nan</c> or empty for missing.</summary>
internal sealed class CsvTable(IReadOnlyList<CsvTable.Row> rows)
{
    public IReadOnlyList<Row> Rows { get; } = rows;

    public static CsvTable ReadIfExists(string path) => File.Exists(path) ? Read(path) : new CsvTable([]);

    public static CsvTable Read(string path)
    {
        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0)
        {
            return new CsvTable([]);
        }

        var header = lines[0].Split(',');
        return new CsvTable([.. lines.Skip(1).Select(line => new Row(header
            .Zip(line.Split(','), (name, value) => (name, value))
            .ToDictionary(p => p.name, p => p.value)))]);
    }

    public sealed class Row(IReadOnlyDictionary<string, string> values)
    {
        public string Text(string column) => values.GetValueOrDefault(column) ?? string.Empty;

        public double? Number(string column) =>
            double.TryParse(values.GetValueOrDefault(column), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
                ? value
                : null;
    }
}
