using System;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using FluentAssertions;
using Xunit;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

public class ChatMessageBufferTests
{
    private readonly ChatMessageBuffer _sut;

    public ChatMessageBufferTests()
    {
        _sut = new ChatMessageBuffer();
    }

    [Fact]
    public void Enqueue_ShouldIncreasePendingCount()
    {
        // Act
        _sut.Enqueue(new ChatMessage { Id = Guid.NewGuid() });
        _sut.Enqueue(new ChatMessage { Id = Guid.NewGuid() });

        // Assert
        _sut.PendingCount.Should().Be(2);
    }

    [Fact]
    public void DrainAll_ShouldReturnAllMessagesAndClearBuffer()
    {
        // Arrange
        var msg1 = new ChatMessage { Id = Guid.NewGuid(), Content = "Message 1" };
        var msg2 = new ChatMessage { Id = Guid.NewGuid(), Content = "Message 2" };
        _sut.Enqueue(msg1);
        _sut.Enqueue(msg2);

        // Act
        var result = _sut.DrainAll();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(msg1);
        result.Should().Contain(msg2);
        _sut.PendingCount.Should().Be(0);
    }

    [Fact]
    public void DrainAll_ShouldReturnEmptyList_WhenBufferIsEmpty()
    {
        // Act
        var result = _sut.DrainAll();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        _sut.PendingCount.Should().Be(0);
    }
}
