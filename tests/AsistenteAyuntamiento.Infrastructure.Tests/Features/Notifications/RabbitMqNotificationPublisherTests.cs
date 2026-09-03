using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AsistenteAyuntamiento.Infrastructure.Features.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Notifications;

public class RabbitMqNotificationPublisherTests
{
    private readonly Mock<IConnectionFactory> _connectionFactoryMock;
    private readonly Mock<IConnection> _connectionMock;
    private readonly Mock<IChannel> _channelMock;
    private readonly Mock<ILogger<RabbitMqNotificationPublisher>> _loggerMock;
    private readonly RabbitMqNotificationPublisher _sut;

    public RabbitMqNotificationPublisherTests()
    {
        _connectionFactoryMock = new Mock<IConnectionFactory>();
        _connectionMock = new Mock<IConnection>();
        _channelMock = new Mock<IChannel>();
        _loggerMock = new Mock<ILogger<RabbitMqNotificationPublisher>>();

        _connectionMock.Setup(c => c.IsOpen).Returns(true);
        _connectionMock.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(_channelMock.Object);

        _connectionFactoryMock.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()))
                              .ReturnsAsync(_connectionMock.Object);

        _sut = new RabbitMqNotificationPublisher(_connectionFactoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task NotifyDocumentStatusChangedAsync_ShouldCreateConnectionAndPublishMessage()
    {
        // Arrange
        var documentId = "doc-123";
        var status = "Completed";

        // Act
        await _sut.NotifyDocumentStatusChangedAsync(documentId, status);

        // Assert
        // Verify connection and channel creation
        _connectionFactoryMock.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _connectionMock.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify exchange declaration
        _channelMock.Verify(c => c.ExchangeDeclareAsync(
            "document_notifications_exchange", 
            ExchangeType.Fanout, 
            true, 
            false, 
            null, 
            It.IsAny<bool>(), 
            It.IsAny<bool>(), 
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify publish
        _channelMock.Verify(c => c.BasicPublishAsync(
            "document_notifications_exchange",
            string.Empty,
            false,
            It.Is<BasicProperties>(p => p.Persistent == true),
            It.Is<ReadOnlyMemory<byte>>(b => Encoding.UTF8.GetString(b.ToArray()).Contains("doc-123") && Encoding.UTF8.GetString(b.ToArray()).Contains("Completed")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyDocumentStatusChangedAsync_ShouldNotCreateNewConnection_IfAlreadyConnected()
    {
        // Arrange
        var documentId = "doc-123";
        var status = "Completed";

        // Act - first call
        await _sut.NotifyDocumentStatusChangedAsync(documentId, status);
        
        // Act - second call
        await _sut.NotifyDocumentStatusChangedAsync(documentId, "Failed");

        // Assert
        // Connection factory should only be called once
        _connectionFactoryMock.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        
        // Channel should publish twice
        _channelMock.Verify(c => c.BasicPublishAsync(
            "document_notifications_exchange",
            string.Empty,
            false,
            It.IsAny<BasicProperties>(),
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
