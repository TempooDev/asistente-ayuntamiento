namespace AsistenteAyuntamiento.Application.Common.Background;

public interface IBackgroundWorkQueue
{
    /// <summary>Queues the item. Returns <c>false</c> when the queue is full.</summary>
    bool TryEnqueue(BackgroundWorkItem item);
}
