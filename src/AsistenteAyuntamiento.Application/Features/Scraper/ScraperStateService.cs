namespace AsistenteAyuntamiento.Application.Features.Scraper;

public class ScraperStateService
{
    public bool IsScraping { get; set; } = false;
    public string ScrapeMessage { get; set; } = string.Empty;
}

