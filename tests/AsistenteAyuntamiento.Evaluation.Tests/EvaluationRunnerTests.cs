using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.Pipelines;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace AsistenteAyuntamiento.Evaluation.Tests;

public sealed class EvaluationRunnerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "eval-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<(PipelineType Pipeline, string Question)> _asked = [];

    private static readonly EvaluationDataset Dataset = new(
        [
            new("q01", "¿Plazo?", "Veinte días", ["BOE-A-2026-1"], "empleo-publico"),
            new("q02", "¿Cuánto?", "Dos millones", ["BOE-A-2026-2"], "ayudas"),
        ],
        ["BOE-A-2026-1", "BOE-A-2026-2"], "sha256:d", "sha256:c");

    private EvaluationRunner CreateSut()
    {
        var services = new ServiceCollection();
        foreach (var type in new[] { PipelineType.Baseline, PipelineType.Hierarchical })
        {
            services.AddSingleton<IRagPipeline>(new RecordingPipeline(type, _asked));
        }

        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.IngestionMetrics).Returns(new List<IngestionMetric>
        {
            new IngestionMetric { Pipeline = PipelineType.Baseline, DocumentId = "BOE-A-2026-1", ChunksGenerated = 1, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new IngestionMetric { Pipeline = PipelineType.Baseline, DocumentId = "BOE-A-2026-1", ChunksGenerated = 2, CreatedAt = DateTime.UtcNow },
        }.BuildMockDbSet().Object);
        var vectorStore = new Mock<IVectorStore>();
        vectorStore.Setup(v => v.GetStatsAsync(It.IsAny<VectorCollection>(), It.IsAny<CancellationToken>())).ReturnsAsync(new VectorCollectionStats(1024, 7));
        services.AddScoped(_ => new IndexingReporter(db.Object, vectorStore.Object));

        return new EvaluationRunner(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<EvaluationRunner>.Instance);
    }

    private EvaluationRunSettings Settings() => new(_directory, [PipelineType.Baseline, PipelineType.Hierarchical], 42,
        new RunInfo("run1", DateTime.UtcNow, null, "abc", 42, "ollama", "qwen2.5:7b-instruct-q4_K_M", 0, "qwen3-embedding:0.6b", null,
            [PipelineType.Baseline, PipelineType.Hierarchical], 2, "sha256:d", "sha256:c"));

    [Fact]
    public async Task Writes_one_answer_per_question_and_pipeline_and_the_run_files()
    {
        await CreateSut().RunAsync(Dataset, Settings());

        var lines = File.ReadAllLines(Path.Combine(_directory, EvaluationRunner.AnswersFile));
        lines.Should().HaveCount(4);
        var first = JsonDocument.Parse(lines[0]).RootElement;
        first.GetProperty("question_id").GetString().Should().Be("q01");
        first.GetProperty("position").GetInt32().Should().Be(0);
        first.GetProperty("warmup").GetBoolean().Should().BeTrue();
        first.GetProperty("contexts")[0].GetProperty("document_id").GetString().Should().Be("BOE-A-2026-1");
        first.GetProperty("timings").GetProperty("ttft_ms").GetDouble().Should().Be(3);
        first.GetProperty("error_code").ValueKind.Should().Be(JsonValueKind.Null);

        var run = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, EvaluationRunner.RunFile))).RootElement;
        run.GetProperty("finished_at").ValueKind.Should().Be(JsonValueKind.String);
        run.GetProperty("pipelines")[0].GetString().Should().Be("Baseline");

        var indexing = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, EvaluationRunner.IndexingFile))).RootElement;
        var baseline = indexing.GetProperty("pipelines")[0];
        baseline.GetProperty("points").GetInt64().Should().Be(7);
        baseline.GetProperty("documents").EnumerateArray().Should().ContainSingle("only the latest metric per document counts")
            .Which.GetProperty("chunks").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Resuming_skips_the_answers_already_written()
    {
        var sut = CreateSut();
        await sut.RunAsync(Dataset with { Questions = Dataset.Questions.Take(1).ToList() }, Settings());
        _asked.Clear();

        await sut.RunAsync(Dataset, Settings());

        _asked.Select(a => a.Question).Should().Equal("¿Cuánto?", "¿Cuánto?");
        File.ReadAllLines(Path.Combine(_directory, EvaluationRunner.AnswersFile)).Should().HaveCount(4);
    }

    [Fact]
    public void Arguments_parse_pipelines_and_limits()
    {
        var args = EvaluationArguments.Parse(["--pipelines", "hierarchical", "--limit", "3", "--seed", "7"]);

        args.Pipelines.Should().Equal(PipelineType.Hierarchical);
        args.Limit.Should().Be(3);
        args.Seed.Should().Be(7);
        EvaluationArguments.Parse([]).Pipelines.Should().Equal(PipelineType.Baseline, PipelineType.Hierarchical);
        FluentActions.Invoking(() => EvaluationArguments.Parse(["--pipelines", "semantic"])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => EvaluationArguments.Parse(["--model", "x"])).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Dataset_reads_questions_and_corpus_ids()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, EvaluationDataset.QuestionsFile),
            """{"id":"q01","question":"¿Plazo?","reference":"Veinte días","reference_document_ids":["BOE-A-2026-1"],"category":"empleo-publico"}""" + "\n");
        File.WriteAllText(Path.Combine(_directory, EvaluationDataset.CorpusFile), "json/BOE/BOE-A-2026-1.json\njson/BOJA/BOJA-2026-1-1.json\n");

        var dataset = EvaluationDataset.Load(_directory);

        dataset.Questions.Should().ContainSingle().Which.ReferenceDocumentIds.Should().Equal("BOE-A-2026-1");
        dataset.CorpusDocumentIds.Should().Equal("BOE-A-2026-1", "BOJA-2026-1-1");
        dataset.DatasetHash.Should().StartWith("sha256:");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingPipeline(PipelineType type, List<(PipelineType, string)> asked) : IRagPipeline
    {
        public PipelineType Type => type;

        public IAsyncEnumerable<string> StreamAsync(ChatTurn turn, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RagEvaluationResult> RunForEvaluationAsync(EvaluationTurn turn, CancellationToken cancellationToken = default)
        {
            asked.Add((type, turn.Question));
            return Task.FromResult(new RagEvaluationResult(type, "respuesta", [new RetrievedContext("BOE-A-2026-1", 0.8, "texto")], "contexto",
                TokenUsageInfo.Of(10, 2), new RagEvaluationTimings(0, 1, 3, 4, 5)));
        }
    }
}
