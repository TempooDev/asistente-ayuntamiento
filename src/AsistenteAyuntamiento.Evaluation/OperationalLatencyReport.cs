using System.Globalization;
using System.Text;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Domain.Common.Enums;
using Npgsql;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>
/// Latency and failure figures of the running platform over a date range (spec: rag-evaluation, Operational Latency
/// Report), for chapter 8 of the thesis. It reads only aggregates of numeric and status columns (never questions,
/// answers or battle text, never user or tenant ids) through a read-only connection, and writes one CSV per measure.
/// </summary>
public sealed class OperationalLatencyReport(string connectionString)
{
    /// <summary>Environment variable with the connection string; a read-only database role is recommended.</summary>
    public const string ConnectionVariable = "EVAL_LATENCY_DB";

    /// <summary>
    /// Time zone of the platform's users: <c>--from</c>/<c>--to</c> are local dates, as the thesis periods are (the
    /// metrics page uses UTC days, so its figures for the same dates can differ by the calls of an hour or two).
    /// The Windows name is the fallback where IANA names are not available.
    /// </summary>
    public static readonly TimeZoneInfo PeriodTimeZone =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Madrid", out var madrid) ? madrid : TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    // Same labels as the metrics dashboard (AiMetricsQueryService.PipelineLabel): no pipeline means a baseline chat
    // answer, and the Arena slots are merged because a slot is a random side, not a RAG pipeline.
    private const string PipelineLabel = $"""
        CASE WHEN COALESCE("Pipeline", '') IN ('', '{LlmPipelines.Baseline}') THEN '{LlmPipelines.Baseline}'
             WHEN "Pipeline" IN ('{LlmPipelines.ArenaAlfa}', '{LlmPipelines.ArenaBeta}') THEN 'arena'
             ELSE "Pipeline" END
        """;

    // Percentiles with percentile_cont over successful calls, as in the evaluation and the dashboard (a failure's
    // duration is the time to the error); durations in milliseconds; "to" is exclusive.
    // Chat answers are what users waited for: background shadow runs and evaluation calls are left out (shadow battles
    // still appear in the Arena sides, where both latencies are real pipeline runs). LLM calls cover every step.
    internal const string ChatAnswersSql = $"""
        SELECT {PipelineLabel} AS pipeline,
               COUNT(*) AS answers,
               ROUND(AVG(CASE WHEN "Success" THEN 0 ELSE 1 END)::numeric, 4) AS failed_share,
               ROUND(AVG("DurationMs") FILTER (WHERE "Success")::numeric, 1) AS mean_ms,
               ROUND((percentile_cont(0.5) WITHIN GROUP (ORDER BY "DurationMs") FILTER (WHERE "Success"))::numeric, 1) AS p50_ms,
               ROUND((percentile_cont(0.95) WITHIN GROUP (ORDER BY "DurationMs") FILTER (WHERE "Success"))::numeric, 1) AS p95_ms,
               COUNT("TimeToFirstTokenMs") FILTER (WHERE "Success") AS ttft_n,
               ROUND((percentile_cont(0.5) WITHIN GROUP (ORDER BY "TimeToFirstTokenMs") FILTER (WHERE "Success"))::numeric, 1) AS ttft_p50_ms,
               ROUND((percentile_cont(0.95) WITHIN GROUP (ORDER BY "TimeToFirstTokenMs") FILTER (WHERE "Success"))::numeric, 1) AS ttft_p95_ms
        FROM chat."AiCallLogs"
        WHERE "Operation" = '{LlmOperations.Answer}' AND "CreatedAt" >= @from AND "CreatedAt" < @to
          AND COALESCE("Pipeline", '') NOT IN ('{LlmPipelines.Evaluation}', '{LlmPipelines.Shadow}')
        GROUP BY 1 ORDER BY 1
        """;

    internal const string LlmCallsSql = $"""
        SELECT {PipelineLabel} AS pipeline, "Operation" AS operation,
               COUNT(*) AS calls,
               ROUND(AVG(CASE WHEN "Success" THEN 0 ELSE 1 END)::numeric, 4) AS failed_share,
               ROUND((percentile_cont(0.5) WITHIN GROUP (ORDER BY "DurationMs") FILTER (WHERE "Success"))::numeric, 1) AS p50_ms,
               ROUND((percentile_cont(0.95) WITHIN GROUP (ORDER BY "DurationMs") FILTER (WHERE "Success"))::numeric, 1) AS p95_ms
        FROM chat."AiCallLogs"
        WHERE "CreatedAt" >= @from AND "CreatedAt" < @to AND COALESCE("Pipeline", '') <> '{LlmPipelines.Evaluation}'
        GROUP BY 1, 2 ORDER BY 1, 2
        """;

    internal const string ArenaSidesSql = """
        WITH sides AS (
            SELECT "LeftSystem" AS system, "LeftLatencyMs" AS latency FROM arena."ArenaBattles" WHERE "CreatedAt" >= @from AND "CreatedAt" < @to
            UNION ALL
            SELECT "RightSystem", "RightLatencyMs" FROM arena."ArenaBattles" WHERE "CreatedAt" >= @from AND "CreatedAt" < @to)
        SELECT system, COUNT(*) AS sides,
               ROUND(AVG(latency)::numeric, 1) AS mean_ms,
               ROUND((percentile_cont(0.5) WITHIN GROUP (ORDER BY latency))::numeric, 1) AS p50_ms,
               ROUND((percentile_cont(0.95) WITHIN GROUP (ORDER BY latency))::numeric, 1) AS p95_ms
        FROM sides GROUP BY system ORDER BY system
        """;

    // As on the metrics page, the latest ingestion of each document and pipeline in the period: a reprocessed document
    // counts once ("ingestions" still counts every run).
    internal const string IngestionSql = """
        WITH metrics AS (
            SELECT "Pipeline", "DocumentId", "ProcessingDurationMs",
                   ROW_NUMBER() OVER (PARTITION BY "DocumentId", "Pipeline" ORDER BY "Id" DESC) AS latest,
                   COUNT(*) OVER (PARTITION BY "DocumentId", "Pipeline") AS runs
            FROM ingestion."IngestionMetrics"
            WHERE "CreatedAt" >= @from AND "CreatedAt" < @to)
        SELECT "Pipeline" AS system, SUM(runs) AS ingestions, COUNT(*) AS documents,
               ROUND(AVG("ProcessingDurationMs")::numeric, 1) AS mean_ms,
               ROUND((percentile_cont(0.5) WITHIN GROUP (ORDER BY "ProcessingDurationMs"))::numeric, 1) AS p50_ms,
               ROUND((percentile_cont(0.95) WITHIN GROUP (ORDER BY "ProcessingDurationMs"))::numeric, 1) AS p95_ms
        FROM metrics WHERE latest = 1
        GROUP BY 1 ORDER BY 1
        """;

    // A snapshot of the current job states last touched in the period: finished documents only (queued or pending
    // ones are not failures yet). A later retry moves a document to the period of that retry.
    internal const string IngestionFailuresSql = """
        SELECT pipeline, COUNT(*) FILTER (WHERE status IN ('Completed', 'Failed', 'VectorSyncFailed')) AS documents,
               COUNT(*) FILTER (WHERE status IN ('Failed', 'VectorSyncFailed')) AS failed,
               ROUND((COUNT(*) FILTER (WHERE status IN ('Failed', 'VectorSyncFailed')))::numeric
                     / NULLIF(COUNT(*) FILTER (WHERE status IN ('Completed', 'Failed', 'VectorSyncFailed')), 0), 4) AS failed_share
        FROM ingestion."DocumentJobStates" j
        CROSS JOIN LATERAL (VALUES ('Baseline', j."BaselineStatus"), ('Hierarchical', j."HierarchicalStatus"),
                                   ('Recursive', j."RecursiveStatus"), ('Semantic', j."SemanticStatus")) AS p(pipeline, status)
        WHERE j."LastUpdatedAt" >= @from AND j."LastUpdatedAt" < @to
        GROUP BY pipeline ORDER BY pipeline
        """;

    /// <summary>The CSV files written, keyed by measure.</summary>
    public static readonly IReadOnlyDictionary<string, string> Measures = new Dictionary<string, string>
    {
        ["chat-answers.csv"] = ChatAnswersSql,
        ["llm-calls.csv"] = LlmCallsSql,
        ["arena-sides.csv"] = ArenaSidesSql,
        ["ingestion.csv"] = IngestionSql,
        ["ingestion-failures.csv"] = IngestionFailuresSql,
    };

    public async Task<IReadOnlyList<string>> WriteAsync(DateOnly from, DateOnly to, string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        // Even with a role that could write, this session cannot.
        await using (var readOnly = new NpgsqlCommand("SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY", connection))
        {
            await readOnly.ExecuteNonQueryAsync(cancellationToken);
        }

        // Every query runs before any file is written, so a failure never leaves new files beside stale ones.
        var files = new Dictionary<string, string>();
        foreach (var (file, sql) in Measures)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("from", StartUtc(from));
            command.Parameters.AddWithValue("to", StartUtc(to));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var csv = new StringBuilder();
            csv.AppendLine(string.Join(',', Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)));
            while (await reader.ReadAsync(cancellationToken))
            {
                csv.AppendLine(string.Join(',', Enumerable.Range(0, reader.FieldCount).Select(i => Cell(reader, i))));
            }

            files[file] = csv.ToString();
        }

        var written = new List<string>();
        foreach (var (file, csv) in files)
        {
            var path = Path.Combine(directory, file);
            await File.WriteAllTextAsync(path, csv, cancellationToken);
            written.Add(path);
        }

        return written;
    }

    /// <summary>Local midnight of a date in <see cref="PeriodTimeZone"/>, as the UTC instant the timestamps are stored in.</summary>
    public static DateTime StartUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), PeriodTimeZone);

    /// <summary>Pipelines are stored as numbers in the Arena and ingestion tables: written by name.</summary>
    private static string Cell(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return string.Empty;
        }

        var value = reader.GetValue(ordinal);
        if (reader.GetName(ordinal) == "system" && value is short or int)
        {
            return ((PipelineType)Convert.ToInt32(value, CultureInfo.InvariantCulture)).ToString();
        }

        // Pipeline and operation labels are free text: quoted as RFC 4180 when they could break the row.
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
    }
}
