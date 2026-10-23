using System.Diagnostics;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>Command-line options of the runner.</summary>
/// <param name="Import">Run id to store in the database instead of running (<c>--import</c>).</param>
/// <param name="Score">After the run, score it, build the reports and import it (<c>--score</c>).</param>
public sealed record EvaluationArguments(IReadOnlyList<PipelineType> Pipelines, int? Limit, int? Seed, string? Resume, string? Dataset, string? Output,
    string? Import = null, bool Score = false)
{
    /// <summary>Options without a value.</summary>
    private static readonly HashSet<string> Switches = new(StringComparer.OrdinalIgnoreCase) { "score" };

    public static EvaluationArguments Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Count; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Expected '--name value' pairs, got '{args[i]}'.");
            }
            var name = args[i][2..];
            if (Switches.Contains(name))
            {
                values[name] = "true";
                continue;
            }
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"--{name} needs a value.");
            }
            values[name] = args[++i];
        }

        var unknown = values.Keys.Except(["pipelines", "limit", "seed", "resume", "dataset", "output", "import", "score"], StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"Unknown option(s): {string.Join(", ", unknown.Select(u => "--" + u))}.");
        }

        return new EvaluationArguments(
            EvaluationVariants.Parse(values.GetValueOrDefault("pipelines")),
            values.TryGetValue("limit", out var limit) ? int.Parse(limit) : null,
            values.TryGetValue("seed", out var seed) ? int.Parse(seed) : null,
            values.GetValueOrDefault("resume"),
            values.GetValueOrDefault("dataset"),
            values.GetValueOrDefault("output"),
            values.GetValueOrDefault("import"),
            values.ContainsKey("score"));
    }

    /// <summary>The folder with <c>AsistenteAyuntamiento.slnx</c>, from the working directory or the binaries upwards.</summary>
    public static string FindRepository()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AsistenteAyuntamiento.slnx")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("AsistenteAyuntamiento.slnx not found; pass --dataset and --output.");
    }

    /// <summary>The checked-out commit, or null without git.</summary>
    public static string? GitCommit(string repository)
    {
        try
        {
            using var git = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            var commit = git?.StandardOutput.ReadToEnd().Trim();
            git?.WaitForExit();
            return git?.ExitCode == 0 ? commit : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
