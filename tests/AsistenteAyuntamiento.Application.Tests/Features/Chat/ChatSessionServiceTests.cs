using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AsistenteAyuntamiento.Application.Tests.Features.Chat;

public class ChatSessionServiceTests
{
    private readonly Mock<IAppDbContext> _dbContextMock;
    private readonly ChatMessageBuffer _buffer;
    private readonly ChatSessionService _sut;

    public ChatSessionServiceTests()
    {
        _dbContextMock = new Mock<IAppDbContext>();
        _buffer = new ChatMessageBuffer();

        // Setup DbSets
        var chatSessionsMock = new Mock<DbSet<ChatSession>>();
        _dbContextMock.Setup(x => x.ChatSessions).Returns(chatSessionsMock.Object);

        var chatMessagesMock = new Mock<DbSet<ChatMessage>>();
        _dbContextMock.Setup(x => x.ChatMessages).Returns(chatMessagesMock.Object);

        _sut = new ChatSessionService(_dbContextMock.Object, _buffer);
    }

    [Fact]
    public async Task CreateNewSessionAsync_ShouldReturnNewSession_AndCallSaveChanges()
    {
        // Arrange
        var userId = "user-123";
        var tenantId = "tenant-456";

        // Act
        var result = await _sut.CreateNewSessionAsync(userId, tenantId);

        // Assert
        result.Should().NotBeNull();
        result.UserId.Should().Be(userId);
        result.TenantId.Should().Be(tenantId);
        result.Id.Should().NotBeEmpty();
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        result.Messages.Should().NotBeNull().And.BeEmpty();

        // Verify DbContext interactions
        _dbContextMock.Verify(x => x.ChatSessions.Add(It.IsAny<ChatSession>()), Times.Once);
        _dbContextMock.Verify(x => x.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public void AddUserMessage_ShouldAddMessageToSession_AndReturnIt()
    {
        // Arrange
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = "user",
            TenantId = "tenant",
            Messages = new List<ChatMessage>()
        };
        var content = "Hello world";

        // Act
        var message = _sut.AddUserMessage(session, content);

        // Assert
        message.Should().NotBeNull();
        message.Role.Should().Be("user");
        message.Content.Should().Be(content);
        message.SessionId.Should().Be(session.Id);
        
        session.Messages.Should().Contain(message);
        _dbContextMock.Verify(x => x.ChatMessages.Add(message), Times.Once);
    }

    [Fact]
    public void GetCompactedHistory_ShouldReturnMessagesChronologically_AndRespectLimit()
    {
        // Arrange
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            Messages = new List<ChatMessage>()
        };

        // Add 25 messages, older to newer
        var baseTime = DateTime.UtcNow.AddMinutes(-30);
        for (int i = 1; i <= 25; i++)
        {
            session.Messages.Add(new ChatMessage
            {
                Id = Guid.NewGuid(),
                Role = "user",
                Content = $"Message {i}",
                CreatedAt = baseTime.AddMinutes(i)
            });
        }

        // Act
        var result = _sut.GetCompactedHistory(session, maxMessages: 20);

        // Assert
        result.Should().HaveCount(20);
        // The first message should be Message 6 (since 1-5 are trimmed)
        result.First().Content.Should().Be("Message 6");
        result.Last().Content.Should().Be("Message 25");
        
        // Ensure chronological order
        result.Should().BeInAscendingOrder(x => x.CreatedAt);
    }
}
