using AsistenteAyuntamiento.Domain.Features.Chat.Entities;
using AsistenteAyuntamiento.Application.Common.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Application.Features.Chat;

/// <summary>
/// Service encapsulating database operations for chat sessions and chat messages.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ChatSessionService"/> class.
/// </remarks>
/// <param name="dbContext">The database context.</param>
/// <param name="buffer">The chat message buffer for async persistence.</param>
public class ChatSessionService(IAppDbContext dbContext, ChatMessageBuffer buffer) : IChatSessionService
{
    private readonly IAppDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly ChatMessageBuffer _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));


    /// <summary>
    /// Explicitly creates a brand new chat session, bypassing the 7-day reuse logic.
    /// </summary>
    public async Task<ChatSession> CreateNewSessionAsync(string userId, string tenantId)
    {
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow,
            Messages = new List<ChatMessage>()
        };

        _dbContext.ChatSessions.Add(session);
        await _dbContext.SaveChangesAsync();

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

    /// <summary>
    /// Creates and adds a user message to the session in-memory, and enqueues for async persistence.
    /// </summary>
    public void EnqueueUserMessage(ChatSession session, string content)
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
        _buffer.Enqueue(message);
    }

    /// <summary>
    /// Creates and adds an assistant message to the session in-memory, and enqueues for async persistence.
    /// </summary>
    public void EnqueueAssistantMessage(ChatSession session, string content)
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
        _buffer.Enqueue(message);
    }

    /// <summary>
    /// Gets a user's chat sessions ordered by creation date descending.
    /// </summary>
    public async Task<List<ChatSession>> GetUserSessionsAsync(string userId, string tenantId)
    {
        return await _dbContext.ChatSessions
            .AsNoTracking()
            .Include(s => s.Messages)
            .Where(s => s.UserId == userId && s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets messages for a specific session.
    /// </summary>
    public async Task<List<ChatMessage>> GetSessionMessagesAsync(Guid sessionId, string userId, string tenantId)
    {
        var session = await _dbContext.ChatSessions
            .AsNoTracking()
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.TenantId == tenantId);

        if (session == null)
        {
            return new List<ChatMessage>();
        }

        return session.Messages.OrderBy(m => m.CreatedAt).ToList();
    }

    public async Task<bool> DeleteSessionAsync(Guid sessionId, string userId, string tenantId)
    {
        var session = await _dbContext.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.TenantId == tenantId);
        if (session != null)
        {
            _dbContext.ChatSessions.Remove(session);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets a specific chat session by ID.
    /// </summary>
    public async Task<ChatSession?> GetSessionByIdAsync(Guid sessionId, string userId, string tenantId)
    {
        return await _dbContext.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.TenantId == tenantId);
    }
}
