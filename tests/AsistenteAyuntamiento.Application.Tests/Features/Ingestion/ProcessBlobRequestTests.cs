using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;
using FluentAssertions;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion;

/// <summary>A request without a source uses the one in the blob path (spec: ingestion-administration).</summary>
public class ProcessBlobRequestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_missing_source_is_inferred_from_the_path(string? source) =>
        new ProcessBlobRequest { BlobPath = "json/BOJA/BOJA-2026-1-1.json", Source = source }.ResolveSource().Should().Be("BOJA");

    [Fact]
    public void The_source_is_no_longer_Unknown_by_default() =>
        new ProcessBlobRequest().Source.Should().BeNull();

    [Fact]
    public void A_given_source_wins_over_the_path() =>
        new ProcessBlobRequest { BlobPath = "json/BOJA/x.json", Source = "BOE" }.ResolveSource().Should().Be("BOE");

    [Theory]
    [InlineData("json/BOE/BOE-A-2026-1.json", "BOE")]
    [InlineData("BOE/BOE-A-2026-1.json", "BOE")]
    [InlineData("otros/BOJA/x.json", "BOJA")]
    [InlineData("x.json", "S3")]
    public void Infers_the_source_from_the_folder(string blobPath, string expected) =>
        DocumentMessage.InferSource(blobPath).Should().Be(expected);
}
