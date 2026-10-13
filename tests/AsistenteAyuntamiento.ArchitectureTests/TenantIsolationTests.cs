using FluentAssertions;

namespace AsistenteAyuntamiento.ArchitectureTests;

/// <summary><c>IgnoreQueryFilters</c> bypasses tenant isolation, so every use must be reviewed and allow-listed.</summary>
public class TenantIsolationTests
{
    /// <summary>Files allowed to call IgnoreQueryFilters, each with an explicit TenantId predicate or an admin-only use.</summary>
    private static readonly string[] AllowList =
    [
        "src/AsistenteAyuntamiento.Application/Features/AiConfig/AiConfigurationService.cs",
        "src/AsistenteAyuntamiento.Application/Features/Arena/ArenaAnalyticsService.cs",
        "src/AsistenteAyuntamiento.Application/Features/Chat/WinningPipelineSelector.cs",
        "src/AsistenteAyuntamiento.Application/Features/Users/UserPreferenceService.cs",
        "src/AsistenteAyuntamiento.Application/Features/Users/UserProfileService.cs",
    ];

    [Fact]
    public void IgnoreQueryFilters_is_only_used_in_allow_listed_files()
    {
        var offenders = new[] { "AsistenteAyuntamiento.Application", "AsistenteAyuntamiento.ApiService", "AsistenteAyuntamiento.Infrastructure", "AsistenteAyuntamiento.Worker" }
            .SelectMany(RepositoryFiles.SourceFiles)
            .Where(f => File.ReadAllText(f).Contains(".IgnoreQueryFilters(", StringComparison.Ordinal))
            .Select(RepositoryFiles.Relative)
            .Except(AllowList);

        offenders.Should().BeEmpty("IgnoreQueryFilters skips the tenant filter; add an explicit TenantId predicate and allow-list the file");
    }
}
