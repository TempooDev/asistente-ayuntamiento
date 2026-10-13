using AsistenteAyuntamiento.Application.Features.Chat;
using FluentAssertions;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

/// <summary>The conversation a follow-up question carries into the hierarchical search and answer.</summary>
public class ConversationContextTests
{
    [Fact]
    public void The_first_question_has_no_previous_messages()
    {
        var history = new ChatHistory();
        history.AddUserMessage("¿Qué convenio firma el Ministerio de Sanidad?");

        ConversationContext.Previous(history).Should().BeEmpty();
        ConversationContext.Describe([]).Should().Be("(ninguna)");
    }

    [Fact]
    public void The_messages_before_the_question_are_kept_without_system_ones()
    {
        var history = new ChatHistory("instrucciones del sistema");
        history.AddUserMessage("Háblame del Convenio entre el Ministerio de Sanidad y la Comunidad de Madrid");
        history.AddAssistantMessage("El convenio BOE-A-2026-18557 regula...");
        history.AddUserMessage("¿Quién preside la Comisión de Seguimiento?");

        var previous = ConversationContext.Previous(history);

        previous.Select(m => m.Role).Should().Equal(AuthorRole.User, AuthorRole.Assistant);
        ConversationContext.Describe(previous).Should().Be(
            "Usuario: Háblame del Convenio entre el Ministerio de Sanidad y la Comunidad de Madrid\n" +
            "Asistente: El convenio BOE-A-2026-18557 regula...");
    }

    [Fact]
    public void Only_the_latest_messages_are_kept_and_long_ones_are_cut()
    {
        var history = new ChatHistory();
        for (var i = 0; i < 10; i++)
        {
            history.AddUserMessage($"pregunta {i}");
            history.AddAssistantMessage(new string('r', 5_000));
        }
        history.AddUserMessage("última");

        var previous = ConversationContext.Previous(history);

        previous.Should().HaveCount(ConversationContext.MaxMessages);
        previous[0].Content.Should().Be("pregunta 7");
        previous[1].Content!.Length.Should().BeLessThan(ConversationContext.MaxCharsPerMessage + 10);
    }
}
