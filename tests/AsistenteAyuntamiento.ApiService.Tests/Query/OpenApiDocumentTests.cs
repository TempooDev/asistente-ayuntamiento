using System.Text.Json;
using System.Text.Json.Nodes;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;

namespace AsistenteAyuntamiento.ApiService.Tests.Query;

/// <summary>
/// The OpenAPI contract versioned in <c>docs/api/openapi.v1.json</c> must match what the API publishes, so a change to
/// an endpoint shows up in review. To regenerate it after an intended change:
/// <c>UPDATE_OPENAPI=1 dotnet test tests/AsistenteAyuntamiento.ApiService.Tests --filter OpenApiDocumentTests</c>.
/// </summary>
public class OpenApiDocumentTests
{
    private const string RelativePath = "docs/api/openapi.v1.json";

    [Fact]
    public async Task The_versioned_document_matches_the_published_one()
    {
        await using var factory = new ApiFactory();
        var published = Normalize(await factory.CreateClient().GetStringAsync("/openapi/v1.json"));
        var path = Path.Combine(RepositoryRoot(), RelativePath);

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(path, published);
        }

        Assert.True(File.Exists(path), $"{RelativePath} is missing: run the test with UPDATE_OPENAPI=1 to create it.");
        var committed = Normalize(await File.ReadAllTextAsync(path));
        Assert.True(committed == published,
            $"{RelativePath} is out of date: run the test with UPDATE_OPENAPI=1 and commit the file.");
    }

    /// <summary>Drops the test server's address and fixes the formatting, so the file only changes with the contract.</summary>
    private static string Normalize(string json)
    {
        var document = JsonNode.Parse(json)!.AsObject();
        document.Remove("servers");
        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AsistenteAyuntamiento.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (AsistenteAyuntamiento.slnx) not found.");
    }
}
