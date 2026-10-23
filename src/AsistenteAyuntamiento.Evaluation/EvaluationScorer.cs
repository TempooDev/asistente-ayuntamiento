using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>
/// Runs the Python scorer and the reports of <c>eval/ragas</c> on a finished run (<c>--score</c>), so a run can go from
/// answers to stored results in one step. Needs <c>uv</c> on the PATH.
/// </summary>
public sealed class EvaluationScorer(ILogger<EvaluationScorer> logger)
{
    /// <returns>False when a step failed (its output is in the log); the run's files are kept either way.</returns>
    public async Task<bool> ScoreAsync(string repository, string runDirectory, string? ollamaUrl, CancellationToken cancellationToken)
    {
        var ragas = Path.Combine(repository, "eval", "ragas");
        foreach (var script in new[] { "score.py", "report.py" })
        {
            var start = new ProcessStartInfo("uv", ["run", "python", script, runDirectory])
            {
                WorkingDirectory = ragas,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (!string.IsNullOrEmpty(ollamaUrl))
            {
                start.Environment["OLLAMA_URL"] = ollamaUrl;
            }

            logger.LogInformation("Running {Script} on {RunDirectory}", script, runDirectory);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start uv.");
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("{Script}: {Line}", script, e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logger.LogWarning("{Script}: {Line}", script, e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                logger.LogError("{Script} failed with exit code {ExitCode}", script, process.ExitCode);
                return false;
            }
        }

        return true;
    }

    /// <summary>The Ollama base URL from an Aspire connection string (<c>Endpoint=http://…</c>) or a plain URL.</summary>
    public static string? OllamaUrl(string? connectionString) =>
        connectionString?.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase)) is { } endpoint
            ? endpoint["Endpoint=".Length..]
            : connectionString;

    /// <summary>The machine of an Ollama URL (host without port or path); the URL itself when it is not absolute.</summary>
    public static string? OllamaHost(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
}
