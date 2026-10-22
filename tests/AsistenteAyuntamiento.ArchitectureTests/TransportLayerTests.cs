using FluentAssertions;

namespace AsistenteAyuntamiento.ArchitectureTests;

/// <summary>Endpoints, hubs and gRPC services only map transport to Application services (spec: clean-architecture-boundaries).</summary>
public class TransportLayerTests
{
    private static readonly string[] PersistenceMarkers = ["IAppDbContext", "AppDbContext", "Microsoft.EntityFrameworkCore"];

    [Fact]
    public void ApiService_features_do_not_access_the_database()
    {
        var offenders = RepositoryFiles.SourceFiles("AsistenteAyuntamiento.ApiService")
            .Where(f => RepositoryFiles.Relative(f).StartsWith("src/AsistenteAyuntamiento.ApiService/Features/", StringComparison.Ordinal))
            .Where(f => PersistenceMarkers.Any(File.ReadAllText(f).Contains))
            .Select(RepositoryFiles.Relative);

        offenders.Should().BeEmpty("move queries and persistence into an Application service");
    }

    [Fact]
    public void ApiService_does_not_set_system_prompts()
    {
        // The pipelines own the system prompt (RAG instructions, user preferences) and only add it when the history
        // has none: a system message added by the hub silently replaced them.
        string[] markers = ["new ChatHistory(\"", "AddSystemMessage("];
        var offenders = RepositoryFiles.SourceFiles("AsistenteAyuntamiento.ApiService")
            .Where(f => markers.Any(File.ReadAllText(f).Contains))
            .Select(RepositoryFiles.Relative);

        offenders.Should().BeEmpty("add system prompts in the Application pipelines (PromptCatalog)");
    }

    [Fact]
    public void Evaluation_runner_exposes_no_network_endpoint()
    {
        // spec: rag-evaluation, Evaluation Runner. A console host: no web SDK, no Kestrel, no mapped routes.
        var project = "AsistenteAyuntamiento.Evaluation";
        File.ReadAllText(Path.Combine(RepositoryFiles.Project(project), $"{project}.csproj")).Should().NotContain("Microsoft.NET.Sdk.Web");
        RepositoryFiles.SourceFiles(project)
            .Where(f => File.ReadAllText(f) is var text && (text.Contains("WebApplication", StringComparison.Ordinal) || text.Contains(".Map", StringComparison.Ordinal)))
            .Select(RepositoryFiles.Relative)
            .Should().BeEmpty();
    }
}
