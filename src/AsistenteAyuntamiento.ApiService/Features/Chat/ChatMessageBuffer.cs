using AsistenteAyuntamiento.Domain.Features.Scraper;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Features.AiConfig;
using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Domain.Features.Chat;
using AsistenteAyuntamiento.Domain.Features.Users;
using AsistenteAyuntamiento.Application.Common.Interfaces;
namespace AsistenteAyuntamiento.ApiService.Features.Chat;

using System.Collections.Concurrent;
using System.Collections.Generic;

/// <summary>
/// Singleton buffer for holding chat messages in memory until they are persisted.
/// </summary>
public class ChatMessageBuffer
{
    private readonly ConcurrentQueue<ChatMessage> _queue = new();

    /// <summary>
    /// Gets the number of pending messages in the buffer.
    /// </summary>
    public int PendingCount => _queue.Count;

    /// <summary>
    /// Enqueues a message to be persisted.
    /// </summary>
    /// <param name="message">The chat message.</param>
    public void Enqueue(ChatMessage message)
    {
        _queue.Enqueue(message);
    }

    /// <summary>
    /// Dequeues all pending messages atomically and returns them.
    /// </summary>
    /// <returns>A list of drained messages.</returns>
    public List<ChatMessage> DrainAll()
    {
        var messages = new List<ChatMessage>();
        while (_queue.TryDequeue(out var message))
        {
            messages.Add(message);
        }
        return messages;
    }
}
