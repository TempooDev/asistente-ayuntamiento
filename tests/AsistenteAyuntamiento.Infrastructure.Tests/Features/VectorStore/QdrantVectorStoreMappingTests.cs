using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Infrastructure.VectorStore;
using FluentAssertions;
using Qdrant.Client.Grpc;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.VectorStore;

public class QdrantVectorStoreMappingTests
{
    [Fact]
    public void ToPointStruct_MapsIdVectorAndTypedPayload()
    {
        var point = new VectorPoint(42, [0.1f, 0.2f], new Dictionary<string, object>
        {
            [VectorPayloadKeys.DocumentId] = "BOE-A-1",
            [VectorPayloadKeys.ChunkIndex] = 3,
            [VectorPayloadKeys.ParentId] = 7L,
        });

        var result = QdrantVectorStore.ToPointStruct(point);

        result.Id.Num.Should().Be(42UL);
        result.Vectors.Vector.Dense.Data.Should().Equal(0.1f, 0.2f);
        result.Payload[VectorPayloadKeys.DocumentId].StringValue.Should().Be("BOE-A-1");
        result.Payload[VectorPayloadKeys.ChunkIndex].IntegerValue.Should().Be(3);
        result.Payload[VectorPayloadKeys.ParentId].IntegerValue.Should().Be(7);
    }

    [Fact]
    public void ToPointStruct_RejectsUnsupportedPayloadTypes()
    {
        var point = new VectorPoint(1, [1f], new Dictionary<string, object> { ["When"] = DateTime.UtcNow });

        var act = () => QdrantVectorStore.ToPointStruct(point);

        act.Should().Throw<ArgumentException>().WithMessage("*When*");
    }

    [Fact]
    public void ToHit_ExposesPayloadThroughTypedAccessors()
    {
        var scored = new ScoredPoint { Id = new PointId { Num = 9 }, Score = 0.8f };
        scored.Payload[VectorPayloadKeys.Title] = "Ayudas al alquiler";
        scored.Payload[VectorPayloadKeys.ChunkIndex] = 2L;
        scored.Payload[VectorPayloadKeys.PublicationDate] = "2026-10-01T00:00:00.0000000Z";

        var hit = QdrantVectorStore.ToHit(scored);

        hit.Id.Should().Be(9);
        hit.Score.Should().Be(0.8f);
        hit.GetString(VectorPayloadKeys.Title).Should().Be("Ayudas al alquiler");
        hit.GetInt64(VectorPayloadKeys.ChunkIndex).Should().Be(2);
        hit.GetDate(VectorPayloadKeys.PublicationDate).Should().Be(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        hit.GetString("Missing").Should().BeEmpty();
    }

    [Fact]
    public void BuildDocumentFilter_MatchesDocumentIdOrAnyLegacyParentId()
    {
        var filter = QdrantVectorStore.BuildDocumentFilter("BOJA-1", [10, 11]);

        filter.Must.Should().BeEmpty();
        filter.Should.Should().HaveCount(3);
        filter.Should[0].Field.Key.Should().Be(VectorPayloadKeys.DocumentId);
        filter.Should[0].Field.Match.Keyword.Should().Be("BOJA-1");
        filter.Should.Skip(1).Select(c => c.Field.Match.Integer).Should().Equal(10, 11);
    }

    [Fact]
    public void Options_ResolvePhysicalCollectionNames()
    {
        var options = new VectorStoreOptions { ChildFragmentsCollection = "child_fragments_v2" };

        options.NameOf(VectorCollection.DocumentChunks).Should().Be("document_chunks");
        options.NameOf(VectorCollection.ChildFragments).Should().Be("child_fragments_v2");
    }
}
