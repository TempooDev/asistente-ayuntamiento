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
}
