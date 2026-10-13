using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using NetArchTest.Rules;

namespace AsistenteAyuntamiento.ArchitectureTests;

/// <summary>Dependency rule: Domain ← Application ← Infrastructure ← hosts (spec: clean-architecture-boundaries).</summary>
public class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(Domain.Features.Ingestion.DocumentChunk).Assembly;
    private static readonly Assembly Application = typeof(Application.Common.Interfaces.IAppDbContext).Assembly;

    /// <summary>Namespaces that belong to infrastructure SDKs.</summary>
    private static readonly string[] InfrastructureNamespaces =
    [
        "Qdrant", "Amazon", "RabbitMQ", "Npgsql", "NpgsqlTypes", "Pgvector",
        "Microsoft.SemanticKernel.Connectors", "OllamaSharp", "AsistenteAyuntamiento.Infrastructure",
    ];

    [Fact]
    public void Domain_does_not_depend_on_persistence_or_other_layers()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny([.. InfrastructureNamespaces, "Microsoft.EntityFrameworkCore", "AsistenteAyuntamiento.Application"])
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_sdks()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespaces)
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Dependency_rule_detects_sdk_usage_where_it_exists()
    {
        // Control: Infrastructure legitimately uses the SDKs, so the same rule must report it.
        var infrastructure = typeof(Infrastructure.VectorStore.QdrantVectorStore).Assembly;

        var result = Types.InAssembly(infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespaces[..^1])
            .GetResult();

        result.IsSuccessful.Should().BeFalse();
        result.FailingTypeNames.Should().Contain(typeof(Infrastructure.VectorStore.QdrantVectorStore).FullName);
    }

    [Theory]
    [InlineData("AsistenteAyuntamiento.Domain", new string[0])]
    [InlineData("AsistenteAyuntamiento.Application", new[]
    {
        "Microsoft.AspNetCore.DataProtection.Abstractions",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.Configuration.Binder",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.SemanticKernel",
    })]
    public void Project_only_references_allowed_packages(string project, string[] allowed)
    {
        var csproj = XDocument.Load(Path.Combine(RepositoryFiles.Project(project), $"{project}.csproj"));
        var packages = csproj.Descendants("PackageReference").Select(p => (string?)p.Attribute("Include")).OfType<string>();

        packages.Should().BeSubsetOf(allowed, $"{project} must stay free of infrastructure packages; add a port instead");
    }
}
