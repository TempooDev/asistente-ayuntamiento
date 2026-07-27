using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsistenteAyuntamiento.ApiService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

/// <summary>
/// Service encapsulating database operations for chat sessions and chat messages.
/// </summary>
public class ChatSessionService
{
    private readonly AppDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatSessionService"/> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public ChatSessionService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <summary>
    /// Finds the most recent chat session for the specified user and tenant within the last 7 days,
    /// or creates a new one if none exists.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="tenantId">The tenant ID.</param>
    /// <returns>The existing or newly created <see cref="ChatSession"/>.</returns>
    public async Task<ChatSession> GetOrCreateSessionAsync(string userId, string tenantId)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);

        var session = await _dbContext.ChatSessions
            .Include(s => s.Messages)
            .Where(s => s.UserId == userId && s.TenantId == tenantId && s.CreatedAt >= cutoff)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (session == null)
        {
            session = new ChatSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenantId,
                CreatedAt = DateTime.UtcNow,
                Messages = new List<ChatMessage>()
            };

            _dbContext.ChatSessions.Add(session);
        }

        return session;
    }

    /// <summary>
    /// Creates and adds a user message to the session and database context.
    /// </summary>
    /// <param name="session">The chat session.</param>
    /// <param name="content">The message content.</param>
    /// <returns>The created <see cref="ChatMessage"/>.</returns>
    public ChatMessage AddUserMessage(ChatSession session, string content)
    {
        ArgumentNullException.ThrowIfNull(session);

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Session = session,
            Role = "user",
            Content = content,
            CreatedAt = DateTime.UtcNow
        };

        session.Messages.Add(message);
        _dbContext.ChatMessages.Add(message);

        return message;
    }

    /// <summary>
    /// Creates and adds an assistant message to the session and database context.
    /// </summary>
    /// <param name="session">The chat session.</param>
    /// <param name="content">The message content.</param>
    /// <returns>The created <see cref="ChatMessage"/>.</returns>
    public ChatMessage AddAssistantMessage(ChatSession session, string content)
    {
        ArgumentNullException.ThrowIfNull(session);

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Session = session,
            Role = "assistant",
            Content = content,
            CreatedAt = DateTime.UtcNow
        };

        session.Messages.Add(message);
        _dbContext.ChatMessages.Add(message);

        return message;
    }

    /// <summary>
    /// Returns the last N messages ordered by CreatedAt from the session's loaded Messages collection.
    /// </summary>
    /// <param name="session">The chat session containing loaded messages.</param>
    /// <param name="maxMessages">The maximum number of messages to return (default: 20).</param>
    /// <returns>A list of <see cref="ChatMessage"/> objects ordered chronologically by CreatedAt.</returns>
    public List<ChatMessage> GetCompactedHistory(ChatSession session, int maxMessages = 20)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.Messages == null || session.Messages.Count == 0)
        {
            return new List<ChatMessage>();
        }

        return session.Messages
            .OrderByDescending(m => m.CreatedAt)
            .Take(maxMessages)
            .OrderBy(m => m.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public async Task SaveAsync()
    {
        await _dbContext.SaveChangesAsync();
    }
}
