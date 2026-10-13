using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Generation;
using AsistenteAyuntamiento.Application.Features.Retrieval;
using AsistenteAyuntamiento.Domain.Common.Enums;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Generation;

/// <summary>Each parent document is labelled so the model can cite it in "Fuentes consultadas" (spec: hierarchical-retrieval).</summary>
public class ClearLanguageGenerationServiceTests
{
    private static RetrievalResult Boe(long fragmentId = 1, long parentId = 10) => new(
        fragmentId, "fragmento", parentId, "Artículo 1. Se convocan ayudas.", 0.5,
        ParentDocumentId: "BOE-A-2026-1234",
        ParentTitle: "Orden de ayudas al alquiler",
        Bulletin: BulletinType.BOE,
        IssuingBody: "Ministerio de Vivienda",
        PublicationDate: new DateTime(2026, 10, 1));

    [Fact]
    public void A_BOE_document_is_labelled_with_title_bulletin_reference_and_public_url()
    {
        var label = ClearLanguageGenerationService.DocumentLabel(Boe());

        label.Should().Be("[Documento: Orden de ayudas al alquiler | Boletín: BOE | Referencia: BOE-A-2026-1234 | " +
            "Organismo: Ministerio de Vivienda | Fecha: 2026-10-01 | URL: https://www.boe.es/buscar/doc.php?id=BOE-A-2026-1234]");
    }

    [Fact]
    public void A_BOJA_document_uses_the_same_public_url_as_the_baseline()
    {
        var label = ClearLanguageGenerationService.DocumentLabel(new RetrievalResult(1, "x", 2, "texto", 0.1,
            ParentDocumentId: "BOJA-2026-10-1", ParentTitle: "", Bulletin: BulletinType.BOJA));

        label.Should().Be("[Documento: BOJA-2026-10-1 | Boletín: BOJA | Referencia: BOJA-2026-10-1 | URL: https://www.juntadeandalucia.es/eboja.html]");
    }

    [Fact]
    public async Task The_prompt_labels_each_distinct_parent_once_instead_of_its_database_id()
    {
        var chat = new Mock<IChatCompletionService>();
        ChatHistory? prompt = null;
        chat.Setup(s => s.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings?>(), It.IsAny<Kernel?>(), It.IsAny<CancellationToken>()))
            .Callback<ChatHistory, PromptExecutionSettings?, Kernel?, CancellationToken>((history, _, _, _) => prompt = history)
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, "respuesta")]);
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(chat.Object);
        var sut = new ClearLanguageGenerationService(builder.Build(), new ConfigurationBuilder().Build(),
            Mock.Of<ILlmUsageMetrics>(), NullLogger<ClearLanguageGenerationService>.Instance);

        await sut.GenerateResponseAsync("¿Qué ayudas hay?", [Boe(fragmentId: 1), Boe(fragmentId: 2)],
            new LlmCallScope("tenant", LlmPipelines.Hierarchical));

        var system = prompt!.First().Content;
        system.Should().Contain("Referencia: BOE-A-2026-1234").And.NotContain("ID Documento Padre");
        system!.Split("--- INICIO DE DOCUMENTO LEGAL ---").Should().HaveCount(2, "the two fragments share one parent");
    }
}
