namespace AsistenteAyuntamiento.Application.Features.Scraper;

/// <summary>
/// In-process flag for the single manual scrape this API instance may run at a time.
/// Start/finish are atomic, so two concurrent triggers cannot both start.
/// Note: it is per instance; the Go scraper must also reject concurrent jobs (audit task 6.4).
/// </summary>
public class ScraperStateService
{
    private int _running;
    private string _message = string.Empty;

    public bool IsScraping => Volatile.Read(ref _running) == 1;

    public string ScrapeMessage => Volatile.Read(ref _message);

    /// <summary>Marks a scrape as running. Returns <c>false</c> if one is already running.</summary>
    public bool TryStart(string message)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }

        Volatile.Write(ref _message, message);
        return true;
    }

    public void Finish()
    {
        Volatile.Write(ref _message, string.Empty);
        Volatile.Write(ref _running, 0);
    }
}
