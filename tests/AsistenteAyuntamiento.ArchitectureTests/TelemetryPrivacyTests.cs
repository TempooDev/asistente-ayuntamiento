using FluentAssertions;

namespace AsistenteAyuntamiento.ArchitectureTests;

/// <summary>
/// LLM telemetry never captures prompt or completion text (spec: observability-grafana, "LLM Traces Without Content
/// by Default"): citizens' questions may contain personal data. Enabling it needs a privacy review and its own change.
/// </summary>
public class TelemetryPrivacyTests
{
    private static readonly string[] SensitiveSwitches =
    [
        "SEMANTICKERNEL_EXPERIMENTAL_GENAI_ENABLE_OTEL_DIAGNOSTICS_SENSITIVE",
        "\"Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive\"",
        "EnableSensitiveData",
    ];

    [Fact]
    public void Sensitive_genai_diagnostics_are_not_enabled_in_settings_compose_files_or_code()
    {
        var offenders = ConfigurationAndCodeFiles()
            .Where(f => SensitiveSwitches.Any(File.ReadAllText(f).Contains))
            .Select(RepositoryFiles.Relative);

        offenders.Should().BeEmpty("prompt and completion text must not reach telemetry without a privacy review");
    }

    [Fact]
    public void ServiceDefaults_enables_the_non_sensitive_genai_diagnostics()
    {
        var extensions = File.ReadAllText(Path.Combine(RepositoryFiles.Project("AsistenteAyuntamiento.ServiceDefaults"), "Extensions.cs"));

        extensions.Should().Contain("\"Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnostics\"");
    }

    private static IEnumerable<string> ConfigurationAndCodeFiles()
    {
        var root = RepositoryFiles.Root;
        var rootFiles = Directory.EnumerateFiles(root, "docker-compose*.yml").Append(Path.Combine(root, ".env.example"));
        var settings = Directory.EnumerateFiles(Path.Combine(root, "src"), "appsettings*.json", SearchOption.AllDirectories);
        var code = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories);
        return rootFiles.Concat(settings).Concat(code)
            .Where(File.Exists)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }
}
