using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Application.Features.Evaluation;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AsistenteAyuntamiento.Evaluation.Tests;

public sealed class EvaluationImporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "eval-import-" + Guid.NewGuid().ToString("N"));
    private readonly string _database = "evaluation-" + Guid.NewGuid();

    private AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_database).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == "any"));

    private async Task WriteScoredRunAsync(string judge = "qwen2.5:7b-instruct-q4_K_M")
    {
        Directory.CreateDirectory(_directory);
        var info = new RunInfo("run1", new DateTime(2026, 10, 22, 21, 30, 0, DateTimeKind.Utc), DateTime.UtcNow, "abc", 42, "ollama",
            "qwen2.5:7b-instruct-q4_K_M", 0, "qwen3-embedding:0.6b", "Retrieve", [PipelineType.Baseline, PipelineType.Hierarchical], 2, "sha256:d", "sha256:c");
        await EvaluationRunner.WriteJsonAsync(Path.Combine(_directory, EvaluationRunner.RunFile), info, CancellationToken.None);

        var question = new EvaluationQuestion("q01", "¿Plazo?", "Veinte días", ["BOE-A-2026-1"], "empleo-publico");
        var answers = new[]
        {
            AnswerLine.From(question, 0, Result(PipelineType.Baseline, 100, 20, null)),
            AnswerLine.From(question, 0, Result(PipelineType.Hierarchical, 300, 40, null)),
            AnswerLine.From(question, 1, Result(PipelineType.Hierarchical, 999, 999, LlmErrorCodes.Unavailable)),
        };
        File.WriteAllLines(Path.Combine(_directory, EvaluationRunner.AnswersFile), answers.Select(a => JsonSerializer.Serialize(a, EvaluationJson.Options)));

        File.WriteAllText(Path.Combine(_directory, EvaluationImporter.SummaryFile),
            "pipeline,n,failed,missing,context_precision_mean,context_precision_std,context_recall_mean,context_recall_std,faithfulness_mean,faithfulness_std,answer_relevancy_mean,answer_relevancy_std,hit@1_mean,hit@3_mean,hit@5_mean,reciprocal_rank_mean,hallucination_rate,no_answer_n,no_answer_detected_rate\n" +
            "Baseline,1,0,0,0.7,0.1,0.6,0.2,0.9,,0.8,,1.0,1.0,1.0,1.0,0.0,0,\n" +
            "Hierarchical,2,1,1,nan,,0.5,0.1,0.75,0.05,0.7,,0.0,1.0,1.0,0.5,0.5,0,\n");
        File.WriteAllText(Path.Combine(_directory, EvaluationImporter.ScoringFile), JsonSerializer.Serialize(new { judge_model = judge, ragas_version = "0.3.9" }));
        File.WriteAllText(Path.Combine(_directory, EvaluationImporter.LatencyFile),
            "pipeline,stage,n,mean_ms,p50_ms,p95_ms\nBaseline,total,1,5000,5000,5000\nBaseline,ttft,1,800,800,800\nHierarchical,total,1,7000,7000,7500\n");
        File.WriteAllText(Path.Combine(_directory, EvaluationImporter.IndexingCsvFile),
            "pipeline,collection,documents,mean_ms_per_document,p50_ms_per_document,p95_ms_per_document,mean_chunks_per_document,embedded_tokens,llm_calls,llm_tokens,points\n" +
            "Baseline,DocumentChunks,17,1200,1000,3000,2.5,9000,0,0,42\n");
    }

    private static RagEvaluationResult Result(PipelineType pipeline, int input, int output, string? error) =>
        new(pipeline, error is null ? "respuesta" : "", [], "", TokenUsageInfo.Of(input, output), new RagEvaluationTimings(0, 1, 2, 3, 4), error);

    [Fact]
    public async Task Imports_the_run_and_one_result_per_pipeline_with_missing_metrics_as_null()
    {
        await WriteScoredRunAsync();
        await using (var db = Context())
        {
            await new EvaluationImporter(db).ImportAsync(_directory);
        }

        await using var read = Context();
        var run = await new EvaluationResultsService(read).GetRunAsync("run1");
        run!.JudgeModel.Should().Be("qwen2.5:7b-instruct-q4_K_M");
        run.RagasVersion.Should().Be("0.3.9");
        run.Results.Should().HaveCount(2);
        var baseline = run.Results.Single(r => r.Pipeline == "Baseline");
        (baseline.Faithfulness, baseline.FaithfulnessStd, baseline.Mrr, baseline.LatencyP50Ms, baseline.TtftP50Ms).Should().Be((0.9, null, 1.0, 5000d, 800d));
        (baseline.MeanTokens, baseline.IndexingP50Ms, baseline.ChunksPerDocument, baseline.Points).Should().Be((120d, 1000d, 2.5, 42L));
        var hierarchical = run.Results.Single(r => r.Pipeline == "Hierarchical");
        (hierarchical.ContextPrecision, hierarchical.Failed, hierarchical.Missing, hierarchical.MeanTokens, hierarchical.TtftP50Ms, hierarchical.Points)
            .Should().Be((null, 1, 1, 340d, null, null), "the failed answer's tokens do not count and there is no indexing row");
    }

    [Fact]
    public async Task Importing_again_replaces_the_run()
    {
        await WriteScoredRunAsync();
        await using (var db = Context())
        {
            await new EvaluationImporter(db).ImportAsync(_directory);
        }
        await WriteScoredRunAsync(judge: "qwen2.5:14b");
        await using (var db = Context())
        {
            await new EvaluationImporter(db).ImportAsync(_directory);
        }

        await using var read = Context();
        var runs = await new EvaluationResultsService(read).ListRunsAsync();
        runs.Should().ContainSingle().Which.JudgeModel.Should().Be("qwen2.5:14b");
        runs[0].Results.Select(r => r.Pipeline).Should().Equal("Baseline", "Hierarchical");
        (await read.EvaluationPipelineResults.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_run_that_was_not_scored_is_refused_without_writing()
    {
        await WriteScoredRunAsync();
        File.Delete(Path.Combine(_directory, EvaluationImporter.SummaryFile));

        await using var db = Context();
        await FluentActions.Invoking(() => new EvaluationImporter(db).ImportAsync(_directory))
            .Should().ThrowAsync<FileNotFoundException>().WithMessage("*summary.csv*");
        (await db.EvaluationRuns.CountAsync()).Should().Be(0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
